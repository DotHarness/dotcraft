import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ComposerContextRecord } from '../../shared/composerContext'
import type { QueuedTurnInput, ConversationTurn } from '../types/conversation'
import { buildComposerInputParts } from '../utils/composeInputParts'
import { projectInputParts } from '../utils/inputPresentation'
import { buildComposerHistory, queuedInputToComposerDraft } from '../utils/composerHistory'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useComposerContextStore } from '../stores/composerContextStore'
import { acceptWelcomeInput, restoreRejectedWelcomeInput } from '../utils/welcomeSubmissionRecovery'

afterEach(() => vi.unstubAllGlobals())

const contexts: ComposerContextRecord[] = [
  { id: 'paste', kind: 'pastedText', path: 'C:/attachments/pasted-text.txt', fileName: 'pasted-text.txt', preview: 'large text', characterCount: 120_000 },
  { id: 'response', kind: 'responseAnnotation', threadId: 'thread', turnId: 'turn', itemId: 'item', selectedText: 'quoted\n</quote>', comment: 'change this' },
  { id: 'diff', kind: 'diffAnnotation', path: 'a.ts', side: 'left', startLine: 2, endLine: 4, selectedText: 'old code', comment: 'keep' },
  { id: 'page', kind: 'pageReference', url: 'https://example.com', title: 'Page', selectionKind: 'region', text: 'chart', comment: 'explain', image: { tempPath: 'C:/region.png', dataUrl: 'data:image/png;base64,AA', fileName: 'region.png', mimeType: 'image/png' } }
]

describe('composer context recovery', () => {
  it('preserves typed references without embedding preview image bytes or flattening context into text', () => {
    const { inputParts } = buildComposerInputParts({ contexts, text: 'my request' })
    expect(projectInputParts(inputParts).contexts.slice(0, 3)).toEqual(contexts.slice(0, 3))
    expect(inputParts.filter(part => part.type === 'text')).toEqual([{ type: 'text', text: 'my request' }])
    expect(JSON.stringify(inputParts)).not.toContain('data:image/png')
  })

  it('rehydrates page captures separately from ordinary images through queue editing', async () => {
    vi.stubGlobal('window', { api: { workspace: { readImageAsDataUrl: vi.fn(async () => ({ dataUrl: 'data:image/png;base64,AA' })) } } })
    const { inputParts } = buildComposerInputParts({ text: 'request', contexts, images: [{ tempPath: 'C:/normal.png', dataUrl: 'data:image/png;base64,BB', fileName: 'normal.png', mimeType: 'image/png' }] })
    const draft = await queuedInputToComposerDraft({ nativeInputParts: inputParts } as QueuedTurnInput)
    expect(draft.contexts).toEqual(contexts)
    expect(draft.images.map((image) => image.tempPath)).toEqual(['C:/normal.png'])
    const rebuilt = buildComposerInputParts(draft).inputParts
    expect(projectInputParts(rebuilt).contexts).toEqual(projectInputParts(inputParts).contexts)
    expect(rebuilt.filter((part) => part.type === 'localImage').map((part) => part.path)).toEqual(['C:/normal.png'])
    expect(draft.text).toBe('request')
  })

  it('recovers context-only native history after serialization', () => {
    const { inputParts } = buildComposerInputParts({ contexts: contexts.slice(0, 3), text: '' })
    const turn = JSON.parse(JSON.stringify({ id: 'turn', threadId: 'thread', items: [{ id: 'item', type: 'userMessage', nativeInputParts: inputParts }] })) as ConversationTurn
    expect(buildComposerHistory([turn], 'thread')[0].contexts).toEqual(contexts.slice(0, 3))
  })

  it('persists only plain draft text across store recreation', () => {
    const data = new Map<string, string>()
    vi.stubGlobal('localStorage', { getItem: (key: string) => data.get(key) ?? null, setItem: (key: string, value: string) => data.set(key, value) })
    useComposerDraftStore.getState().saveDraft('restart', { text: 'keep text', segments: [], images: [], files: [], contexts })
    expect(useComposerDraftStore.getState().getDraft('restart')?.contexts).toEqual(contexts)
    useComposerDraftStore.setState({ draftsByThread: {} })
    expect(useComposerDraftStore.getState().getDraft('restart')?.text).toBe('keep text')
    expect(useComposerDraftStore.getState().getDraft('restart')?.contexts).toBeUndefined()
    useComposerDraftStore.getState().clearDraft('restart')
  })

  it('restores rejected Welcome input and consumes only accepted source identities', async () => {
    useComposerDraftStore.setState({ draftsByThread: {} })
    useComposerContextStore.setState({ byThread: {}, restoreRequests: {} })
    const submitted = contexts.slice(0, 3)
    const input = buildComposerInputParts({ text: 'welcome request', contexts: submitted }).inputParts
    const newer = { ...submitted[0], id: 'newer' }
    useComposerContextStore.getState().setContexts('welcome-created', [...submitted, newer])
    await restoreRejectedWelcomeInput('welcome-created', input)
    expect(useComposerDraftStore.getState().getDraft('welcome-created')?.text).toBe('welcome request')
    expect(useComposerContextStore.getState().restoreRequests['welcome-created']?.contexts).toHaveLength(4)
    acceptWelcomeInput('welcome-created', input)
    expect(useComposerContextStore.getState().getContexts('welcome-created')).toEqual([newer])
  })

  it('merges rejected Welcome references with newer structured input and attachments', async () => {
    vi.stubGlobal('window', { api: { workspace: { readImageAsDataUrl: async () => ({ dataUrl: 'data:image/png;base64,AA' }) } } })
    const image = (tempPath: string) => ({ tempPath, dataUrl: 'data:image/png;base64,AA', fileName: 'image.png', mimeType: 'image/png' })
    useComposerDraftStore.getState().saveDraft('merge', { text: '/review @current.ts', segments: [
      { type: 'command', command: '/review' }, { type: 'text', value: ' ' }, { type: 'file', relativePath: 'current.ts' }
    ], files: [{ path: 'C:/new.txt', fileName: 'new.txt' }], images: [image('C:/shared.png'), image('C:/new.png')] })
    const input = buildComposerInputParts({ text: '', segments: [{ type: 'skill', skillName: 'inspect' }, { type: 'file', relativePath: 'previous.ts' }],
      files: [{ path: 'C:/old.txt', fileName: 'old.txt' }], images: [image('C:/old.png'), image('C:/shared.png')] }).inputParts
    await restoreRejectedWelcomeInput('merge', input)
    const merged = useComposerDraftStore.getState().getDraft('merge')!
    expect(merged.segments).toEqual(expect.arrayContaining([
      { type: 'skill', skillName: 'inspect' }, { type: 'file', relativePath: 'previous.ts' },
      { type: 'command', command: '/review' }, { type: 'file', relativePath: 'current.ts' },
      { type: 'file', relativePath: 'C:/old.txt' }
    ]))
    expect(merged.files.map((file) => file.path)).toEqual(['C:/new.txt'])
    expect(merged.images.map((attachment) => attachment.tempPath)).toEqual(['C:/old.png', 'C:/shared.png', 'C:/new.png'])
  })
})
