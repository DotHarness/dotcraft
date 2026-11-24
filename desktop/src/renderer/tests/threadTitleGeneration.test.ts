import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DiffAnnotationContext } from '../../shared/composerContext'
import { buildComposerInputParts } from '../utils/composeInputParts'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import {
  buildTitleHelperConfiguration,
  generateThreadTitle,
  parseGeneratedTitle,
  threadTitleRequest
} from '../utils/threadTitleGeneration'

const comment: DiffAnnotationContext = {
  id: 'comment-1',
  kind: 'diffAnnotation',
  path: 'src/parser.ts',
  side: 'right',
  startLine: 12,
  endLine: 14,
  diffHunk: '@@ -1 +1 @@\n-old\n+new',
  comment: 'This drops the  last token'
}

describe('provisional thread titles', () => {
  it('names from the user text, keeps references, and leaves the comment section out', () => {
    const built = buildComposerInputParts({
      text: '',
      segments: [
        { type: 'text', value: 'Fix the parser in\n\n ' },
        { type: 'file', relativePath: 'src/parser.ts' }
      ],
      contexts: [comment]
    })
    expect(threadTitleRequest({ bodyText: built.bodyText, comments: [comment], visibleText: built.visibleText })).toEqual({
      provisionalTitle: 'Fix the parser in @src/parser.ts',
      requestText: 'Fix the parser in\n\n @src/parser.ts'
    })
  })

  it('caps the provisional title at 60 characters', () => {
    const request = threadTitleRequest({ bodyText: `${'word '.repeat(20)}end` })
    expect(request!.provisionalTitle).toBe('word '.repeat(12).trimEnd())
  })

  it('names a comment-only message from its comments and files', () => {
    expect(threadTitleRequest({ bodyText: '', comments: [comment] })).toEqual({
      provisionalTitle: 'src/parser.ts: This drops the last token',
      requestText: 'src/parser.ts: This drops the last token'
    })
  })

  it('names a references-only message with the attachment label and generates from the references', () => {
    expect(threadTitleRequest({ bodyText: '', visibleText: '@notes.txt', referencesTitle: 'File reference message' })).toEqual({
      provisionalTitle: 'File reference message',
      requestText: '@notes.txt'
    })
    expect(threadTitleRequest({ bodyText: '  ', visibleText: '', referencesTitle: 'Image message' })).toBeNull()
  })
})

describe('generated title parsing', () => {
  it.each([
    ['{"title":"Fix parser token loss."}', 'Fix parser token loss'],
    ['```json\n{"title": "“Add login page”"}\n```', 'Add login page'],
    ['Rename the config loader', 'Rename the config loader'],
    ['{"title":"A","description":"extra"}', null],
    ['{"name":"Fix"}', null],
    ['First line\nsecond line', null],
    ['{"title": "   "}', null],
    ['not json {', 'not json {']
  ])('parses %j', (response, expected) => {
    expect(parseGeneratedTitle(response)).toBe(expected)
  })

  it('caps the generated title at 36 characters', () => {
    expect(parseGeneratedTitle(`{"title":"${'x'.repeat(50)}"}`)).toBe('x'.repeat(36))
  })
})

describe('title helper thread', () => {
  beforeEach(() => {
    useModelCatalogStore.getState().reset()
    vi.spyOn(console, 'debug').mockImplementation(() => {})
  })
  afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

  it('runs on the source model without tools, MCP, or skills', () => {
    useModelCatalogStore.setState({
      catalogs: new Map([['provider-a', {
        ...useModelCatalogStore.getState().getCatalog('provider-a'),
        providerId: 'provider-a',
        models: [{
          id: 'model-x',
          reasoning: {
            supportsDisable: false,
            supportedEfforts: [
              { effort: 'high', label: 'High', description: '' },
              { effort: 'medium', label: 'Medium', description: '' }
            ],
            defaultEffort: 'high',
            supportedOutputs: ['none', 'summary'],
            defaultOutput: 'summary'
          }
        }]
      }]])
    })

    expect(buildTitleHelperConfiguration({ providerId: 'provider-a', model: 'model-x', mode: 'plan' })).toMatchObject({
      mode: 'agent',
      providerId: 'provider-a',
      model: 'model-x',
      reasoning: { enabled: true, effort: 'medium', output: 'none' },
      approvalPolicy: 'deny',
      mcpServers: [],
      mcpPolicy: { servers: [] },
      toolPolicy: { allow: [] },
      skillsPolicy: { allow: [] }
    })
    expect(buildTitleHelperConfiguration({ providerId: 'other', model: 'model-x' })).not.toHaveProperty('reasoning')
  })

  const source = { connectionKey: 'project', connectionId: 'connection' }

  function stubServer({ answer, names }: { answer: string | null; names: Array<string | null> }) {
    let notify: ((value: { method: string; params: unknown } & typeof source) => void) | null = null
    const reads = [...names]
    const sendRequest = vi.fn().mockImplementation(async (method: string) => {
      if (method === 'thread/read') {
        return { thread: {
          displayName: reads.shift(),
          workspacePath: 'X:\\work',
          userId: 'local',
          channelContext: 'workspace:X:\\work',
          configuration: { providerId: 'provider-a', model: 'model-x' }
        } }
      }
      if (method === 'thread/start') return { thread: { id: 'helper-1' } }
      if (method === 'turn/start') {
        queueMicrotask(() => notify?.(answer == null
          ? { ...source, method: 'turn/failed', params: { turn: { id: 'helper-turn', threadId: 'helper-1', status: 'failed' } } }
          : { ...source, method: 'turn/completed', params: { turn: {
              id: 'helper-turn',
              threadId: 'helper-1',
              status: 'completed',
              items: [{ type: 'agentMessage', payload: { text: answer } }]
            } } }))
        return { turn: { id: 'helper-turn' } }
      }
      return {}
    })
    vi.stubGlobal('window', {
      setTimeout,
      clearTimeout,
      api: { appServer: { onNotification: (callback: typeof notify) => {
        notify = callback
        return () => { notify = null }
      } } }
    })
    return sendRequest
  }

  const request = { provisionalTitle: 'Fix the parser', requestText: 'Fix the parser please' }

  it('retains the ephemeral helper observation until cleanup', async () => {
    const sendRequest = stubServer({ answer: '{"title":"Fix parser token loss"}', names: ['Fix the parser', 'Fix the parser'] })

    await generateThreadTitle('thread-1', request, { request: sendRequest, source })

    expect(sendRequest).toHaveBeenCalledWith('thread/start', expect.objectContaining({ ephemeral: true }),
      expect.any(Number), { attachmentOwner: 'warm' })
    expect(sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'helper-1' })
    expect(sendRequest).toHaveBeenCalledWith('thread/unsubscribe', { threadId: 'helper-1' }, undefined, { attachmentOwner: 'warm' })
  })

  it('keeps a name the user changed while the title was generating', async () => {
    const sendRequest = stubServer({ answer: '{"title":"Fix parser token loss"}', names: ['Fix the parser', 'My own name'] })

    expect(await generateThreadTitle('thread-1', request, { request: sendRequest, source })).toBeNull()
    expect(sendRequest).not.toHaveBeenCalledWith('thread/rename', expect.anything())
    expect(sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'helper-1' })
  })

  it('does not start a helper when the thread no longer carries the provisional name', async () => {
    const sendRequest = stubServer({ answer: '{"title":"Fix parser token loss"}', names: ['Renamed'] })

    expect(await generateThreadTitle('thread-1', request, { request: sendRequest, source })).toBeNull()
    expect(sendRequest.mock.calls.map(([method]) => method)).toEqual(['thread/read'])
  })

  it.each([
    ['a failed helper turn', null],
    ['an unparseable answer', 'Sure! Here is a title:\nFix parser']
  ])('keeps the provisional name after %s', async (_label, answer) => {
    const sendRequest = stubServer({ answer, names: ['Fix the parser', 'Fix the parser'] })

    expect(await generateThreadTitle('thread-1', request, { request: sendRequest, source })).toBeNull()
    expect(sendRequest).not.toHaveBeenCalledWith('thread/rename', expect.anything())
    expect(sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'helper-1' })
  })
})
