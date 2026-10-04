import { afterEach, describe, expect, it } from 'vitest'
import type { FakeComputer } from '../demo/fakeComputer'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { sendDraft } from './attachments'
import { EMPTY_DRAFT, plainMessage, type MessageDraft } from './draft'
import type { MobileState } from './state'

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

async function connected(): Promise<{ computer: FakeComputer; harness: Harness; state: () => MobileState }> {
  const computer = createStudio(new Date())
  computer.streamDelayMs = 1
  const harness = createHarness([computer])
  harnesses.push(harness)
  const state = () => harness.session.store.getState()
  await harness.session.boot()
  await waitFor(() => state().link === 'online' && !state().syncing)
  return { computer, harness, state }
}

async function opened(harness: Harness, state: () => MobileState, title: string): Promise<string> {
  const key = Object.values(state().chats).find((chat) => chat.title === title)!.key
  harness.session.openChat(key)
  await waitFor(() => state().details[key]?.loading === false)
  return key
}

function calls(computer: FakeComputer, ...methods: string[]) {
  return computer.calls.filter((call) => methods.includes(call.method))
}

const FILES: MessageDraft = {
  ...EMPTY_DRAFT,
  text: 'Compare these two.',
  files: [
    { id: 'a', name: 'build.log', dataBase64: 'bG9n' },
    { id: 'b', name: 'notes.md', dataBase64: 'bm90ZXM=' },
  ],
}

describe('file attachments', () => {
  it('creates a folder and writes each file under the project before sending references to them', async () => {
    const { computer, harness, state } = await connected()
    const key = await opened(harness, state, 'Explain the release script')
    await harness.session.send(key, FILES)

    const sequence = calls(computer, 'fs/createDirectory', 'fs/writeFile', 'turn/start')
    expect(sequence.map((call) => call.method)).toEqual(['fs/createDirectory', 'fs/writeFile', 'fs/createDirectory', 'fs/writeFile', 'turn/start'])
    const [firstDirectory, firstFile, secondDirectory, secondFile] = sequence.map((call) => String(call.params.path))
    expect(firstDirectory).toMatch(/^D:\/Projects\/dotcraft\/\.craft\/attachments\/[^/]+$/)
    expect(firstFile).toBe(`${firstDirectory}/build.log`)
    expect(secondFile).toBe(`${secondDirectory}/notes.md`)
    expect(secondDirectory).not.toBe(firstDirectory)
    expect(computer.files.files.get(secondFile)).toBe('bm90ZXM=')
    expect(sequence[4].params.input).toEqual([
      { type: 'fileRef', path: firstFile, displayPath: 'build.log' },
      { type: 'text', text: '\n' },
      { type: 'fileRef', path: secondFile, displayPath: 'notes.md' },
      { type: 'text', text: '\n\n' },
      { type: 'text', text: 'Compare these two.' },
    ])
  })

  it('names the file that failed and sends nothing when an upload fails', async () => {
    const { computer, harness, state } = await connected()
    const key = await opened(harness, state, 'Explain the release script')
    computer.files.blockedNames.add('notes.md')

    expect(await sendDraft(FILES, (draft) => harness.session.send(key, draft))).toEqual({ kind: 'upload', file: 'notes.md' })
    expect(calls(computer, 'turn/start')).toHaveLength(0)
    expect(state().details[key].history.echoes).toHaveLength(0)
  })
})

describe('commands and skills', () => {
  it('lists custom commands and enabled skills only', async () => {
    const { computer, harness, state } = await connected()
    const projectId = state().projects.find((project) => project.name === 'dotcraft')!.id
    await harness.session.loadReferences(projectId)
    expect(calls(computer, 'command/list')[0].params).toEqual({ includeBuiltins: false })
    expect(state().references[projectId].map((entry) => `${entry.kind}:${entry.name}`)).toEqual([
      'command:code-review',
      'command:release-check',
      'command:triage',
      'skill:release-notes',
      'skill:browser',
      'skill:docs-guide',
    ])
  })
})

describe('plan mode', () => {
  it('switches the chat mode with thread/mode/set', async () => {
    const { computer, harness, state } = await connected()
    const key = await opened(harness, state, 'Explain the release script')
    await harness.session.setMode(key, 'plan')
    expect(calls(computer, 'thread/mode/set').map((call) => call.params.mode)).toEqual(['plan'])
    expect(state().details[key].config?.mode).toBe('plan')
  })

  it('implements a pending plan by switching to agent and sending the implement message', async () => {
    const { computer, harness, state } = await connected()
    const key = await opened(harness, state, 'Plan the phone pairing flow')
    expect(state().chats[key].runtime?.waitingOnPlanConfirmation).toBe(true)

    await harness.session.implementPlan(key)
    const sequence = calls(computer, 'thread/mode/set', 'turn/start')
    expect(sequence.map((call) => [call.method, call.params.mode ?? call.params.input])).toEqual([
      ['thread/mode/set', 'agent'],
      ['turn/start', [{ type: 'text', text: 'Implement the plan.' }]],
    ])
    await waitFor(() => state().chats[key].runtime?.running === false)
    expect(state().chats[key].runtime?.waitingOnPlanConfirmation).toBe(false)
  })

  it('sends typed feedback in plan mode without leaving it', async () => {
    const { computer, harness, state } = await connected()
    const key = await opened(harness, state, 'Plan the phone pairing flow')
    await harness.session.send(key, plainMessage('Add a step for revoking a lost phone.'))
    expect(calls(computer, 'thread/mode/set')).toHaveLength(0)
    await waitFor(() => state().chats[key].runtime?.running === false)
    expect(state().details[key].config?.mode).toBe('plan')
  })
})
