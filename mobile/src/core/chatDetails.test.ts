import { afterEach, describe, expect, it } from 'vitest'
import { MEADOW_PNG } from '../demo/images'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { EMPTY_DRAFT, plainMessage } from './draft'
import { persistable, type MobileState } from './state'
import { offersPlanMode } from './threadConfig'
import { buildTranscript } from './transcript'
import { latestChanges } from './turnChanges'

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

async function connected() {
  const computer = createStudio(new Date())
  computer.streamDelayMs = 1
  const harness = createHarness([computer])
  harnesses.push(harness)
  const state = () => harness.session.store.getState()
  await harness.session.boot()
  await waitFor(() => state().link === 'online' && !state().syncing)
  const opened = async (title: string) => {
    const key = Object.values(state().chats).find((chat) => chat.title === title)!.key
    harness.session.openChat(key)
    await waitFor(() => state().details[key]?.loading === false)
    return key
  }
  const projectId = (name: string) => state().projects.find((project) => project.name === name)!.id
  return { computer, harness, state, opened, projectId }
}

describe('context ring', () => {
  it('starts from the thread snapshot, stays empty without one, and follows usage deltas', async () => {
    const { harness, state, opened } = await connected()
    const running = await opened('Fix the flaky turn-diff test')
    expect(state().details[running].context).toEqual({ tokens: 244_000, contextWindow: 400_000, percentLeft: 0.39 })
    const plan = await opened('Plan the phone pairing flow')
    expect(state().details[plan].context).toBeNull()

    const release = await opened('Explain the release script')
    const before = state().details[release].context!.tokens
    await harness.session.send(release, plainMessage('More detail, please.'))
    await waitFor(() => (state().details[release].context?.tokens ?? 0) > before)
    expect(state().details[release].context!.percentLeft).toBeCloseTo(1 - state().details[release].context!.tokens / 400_000, 6)
  })
})

describe('changes', () => {
  it('rebuilds a reopened chat from recorded file changes and takes a running turn from its live diff', async () => {
    const { state, opened } = await connected()
    const done = await opened('Rename the settings segments')
    expect(state().details[done].history.diffs).toBeUndefined()
    const reopened = latestChanges(state().details[done].history, state().details[done].workspacePath)
    expect(reopened?.files.map((file) => [file.path, file.added, file.removed])).toEqual([['locales/de.ts', 4, 4]])

    const running = await opened('Fix the flaky turn-diff test')
    await waitFor(() => Object.keys(state().details[running].history.diffs ?? {}).length > 0)
    const live = latestChanges(state().details[running].history, state().details[running].workspacePath)
    expect(live).toMatchObject({ added: 42, removed: 8 })
    expect(live?.files.map((file) => file.path)).toEqual(['src/turnDiffStore.ts', 'src/turnDiff.test.ts', 'src/snapshotWriter.ts'])
  })
})

describe('files and usage', () => {
  it('reads a file from the computer and reports a file over the read limit', async () => {
    const { harness, projectId } = await connected()
    const project = projectId('dotcraft')
    expect(atob(await harness.session.readFile(project, 'D:/Projects/dotcraft/scripts/release.ps1'))).toMatch(/^param\(/)
    await expect(harness.session.readFile(project, 'D:/Projects/dotcraft/artifacts/release/publish.log')).rejects.toMatchObject({
      data: { code: 'FileTooLarge' },
    })
  })

  it('loads account usage windows and which providers sign in with an account', async () => {
    const { harness, state, projectId } = await connected()
    const project = projectId('dotcraft')
    await harness.session.loadUsage(project)
    const models = state().models[project]
    expect(models.usage?.map((window) => [window.seconds, window.percentLeft])).toEqual([
      [18_000, 73],
      [604_800, 91],
    ])
    expect(models.providers.map((provider) => [provider.id, provider.signsIn])).toEqual([
      ['studio', true],
      ['local', false],
    ])
  })
})

describe('sent photos', () => {
  it('shows photos in the message from the moment it is sent and after the computer records it', async () => {
    const { harness, state, opened } = await connected()
    const key = await opened('Explain the release script')
    const url = `data:image/png;base64,${MEADOW_PNG}`
    const echoed: string[][] = []
    const unsubscribe = harness.session.store.subscribe(() => {
      for (const entry of buildTranscript(state().details[key].history)) {
        if (entry.kind === 'user' && entry.id.startsWith('echo-')) echoed.push(entry.images)
      }
    })
    await harness.session.send(key, { ...EMPTY_DRAFT, text: 'Like this', photos: [{ id: 'p1', dataUrl: url }] })
    unsubscribe()
    expect(echoed[0]).toEqual([url])

    await waitFor(() => state().chats[key].runtime?.running === false)
    const sent = buildTranscript(state().details[key].history).filter((entry) => entry.kind === 'user').at(-1)
    expect(sent).toMatchObject({ kind: 'user', text: 'Like this', images: [url] })

    const stored = persistable(state() as MobileState).details[key].history.items.filter((item) => item.type === 'userMessage').at(-1)
    expect(stored?.payload.nativeInputParts).toEqual([{ type: 'text', text: 'Like this' }])
  })

  it('reads photos from the history of a reopened chat', async () => {
    const { state, opened } = await connected()
    const key = await opened('Rename the settings segments')
    const [user] = buildTranscript(state().details[key].history)
    expect(user).toMatchObject({ kind: 'user', images: [`data:image/png;base64,${MEADOW_PNG}`, `data:image/png;base64,${MEADOW_PNG}`] })
  })
})

describe('agent profiles', () => {
  it('offers Plan mode only to chats that do not run an Agent Profile', async () => {
    const { state, opened } = await connected()
    const review = await opened('Review the phone pairing PR')
    const release = await opened('Explain the release script')
    expect(offersPlanMode(state().details[review].config, state().chats[review].profileId)).toBe(false)
    expect(offersPlanMode(state().details[release].config, state().chats[release].profileId)).toBe(true)
    expect(offersPlanMode({ agentProfileId: 'reviewer' }, null)).toBe(false)
  })
})
