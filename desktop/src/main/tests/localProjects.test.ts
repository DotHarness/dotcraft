import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DesktopHubError, type HubEvent, type HubProject } from '../desktopHub'
import { LocalProjectList } from '../localProjects'

function project(path: string, addedAt: string, lastOpenedAt = addedAt): HubProject {
  return { path, displayName: path.split('/').pop() ?? path, addedAt, lastOpenedAt, running: false }
}

const A = project('/work/a', '2026-10-01T08:00:00Z')
const B = project('/work/b', '2026-10-02T08:00:00Z')
const C = project('/work/c', '2026-10-03T08:00:00Z')

function fakeHub(initial: HubProject[]) {
  let projects = initial
  let emit: ((event: HubEvent) => void) | null = null
  const hub = {
    listProjects: vi.fn(async () => projects),
    openProject: vi.fn(async (path: string) => {
      const existing = projects.find((entry) => entry.path === path)
      const opened = { ...(existing ?? project(path, '2026-10-04T09:00:00Z')), lastOpenedAt: '2026-10-04T09:00:00Z' }
      projects = [opened, ...projects.filter((entry) => entry.path !== path)]
      return opened
    }),
    removeProject: vi.fn(async (path: string) => {
      if (!projects.some((entry) => entry.path === path)) {
        throw new DesktopHubError('projectNotFound', 'Project not found.')
      }
      projects = projects.filter((entry) => entry.path !== path)
    }),
    subscribeEvents: vi.fn((onEvent: (event: HubEvent) => void, signal: AbortSignal) => {
      emit = onEvent
      return new Promise<void>((resolve) => signal.addEventListener('abort', () => resolve(), { once: true }))
    })
  }
  return {
    hub,
    setProjects: (next: HubProject[]) => { projects = next },
    emit: (kind: string) => emit?.({ kind, at: new Date().toISOString() })
  }
}

const paths = (list: LocalProjectList): string[] => list.list().map((entry) => entry.path)

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => { resolve = complete })
  return { promise, resolve }
}

describe('LocalProjectList', () => {
  it('re-reads Hub projects on projects.changed and notifies', async () => {
    const fake = fakeHub([B, A])
    const onChanged = vi.fn()
    const list = new LocalProjectList(() => fake.hub, onChanged)

    list.start()
    await vi.waitFor(() => expect(fake.hub.subscribeEvents).toHaveBeenCalled())
    expect(paths(list)).toEqual(['/work/a', '/work/b'])

    fake.setProjects([C, B, A])
    fake.emit('appserver.running')
    fake.emit('projects.changed')

    await vi.waitFor(() => expect(paths(list)).toEqual(['/work/a', '/work/b', '/work/c']))
    expect(fake.hub.listProjects).toHaveBeenCalledTimes(2)
    expect(onChanged).toHaveBeenCalledTimes(2)
    list.stop()
  })

  it('opens and removes through Hub, ordered by when each project was added', async () => {
    const fake = fakeHub([B, A])
    const list = new LocalProjectList(() => fake.hub, vi.fn())
    await list.refresh()

    await list.open('/work/a')
    expect(fake.hub.openProject).toHaveBeenCalledWith('/work/a')
    expect(paths(list)).toEqual(['/work/a', '/work/b'])

    await list.remove('/work/b')
    expect(fake.hub.removeProject).toHaveBeenCalledWith('/work/b')
    expect(paths(list)).toEqual(['/work/a'])

    await expect(list.remove('/work/b')).resolves.toBeUndefined()
  })

  it('rejects an open that Hub refuses and keeps the last projects while Hub is unreachable', async () => {
    const fake = fakeHub([A])
    fake.hub.openProject.mockRejectedValue(new DesktopHubError('workspaceNotFound', 'Workspace path does not exist.'))
    const onChanged = vi.fn()
    const list = new LocalProjectList(() => fake.hub, onChanged)
    await list.refresh()

    await expect(list.open('/work/missing')).rejects.toThrow('Workspace path does not exist.')
    expect(paths(list)).toEqual(['/work/a'])

    onChanged.mockClear()
    fake.hub.listProjects.mockRejectedValueOnce(new DesktopHubError('hubUnavailable', 'DotCraft Hub could not be started.'))
    await list.refresh()
    expect(paths(list)).toEqual(['/work/a'])
    expect(onChanged).not.toHaveBeenCalled()

    fake.setProjects([A, B])
    await list.refresh()
    expect(paths(list)).toEqual(['/work/a', '/work/b'])
    expect(onChanged).toHaveBeenCalledTimes(1)
  })

  describe('snapshot recovery', () => {
    beforeEach(() => {
      vi.useFakeTimers()
      vi.spyOn(console, 'warn').mockImplementation(() => {})
    })

    afterEach(() => {
      vi.clearAllTimers()
      vi.useRealTimers()
      vi.restoreAllMocks()
    })

    it('recovers all projects after a cold fetch failure while SSE stays connected without events', async () => {
      const projects = Array.from({ length: 7 }, (_, index) => project(`/work/${index}`, `2026-10-0${index + 1}T08:00:00Z`))
      const fake = fakeHub(projects)
      fake.hub.listProjects.mockRejectedValueOnce(new Error('Hub is starting'))
      const onChanged = vi.fn()
      const list = new LocalProjectList(() => fake.hub, onChanged)
      list.start()
      await vi.advanceTimersByTimeAsync(0)
      expect(paths(list)).toEqual([])
      expect(fake.hub.subscribeEvents).toHaveBeenCalledOnce()

      await vi.advanceTimersByTimeAsync(1_999)
      expect(fake.hub.listProjects).toHaveBeenCalledOnce()
      await vi.advanceTimersByTimeAsync(1)
      expect(list.list()).toHaveLength(7)
      expect(onChanged).toHaveBeenCalledOnce()
      expect(fake.hub.subscribeEvents).toHaveBeenCalledOnce()
      expect(vi.getTimerCount()).toBe(0)
      list.stop()
    })

    it('backs off failed snapshots independently and caps the delay until stopped', async () => {
      const fake = fakeHub([A])
      fake.hub.listProjects.mockRejectedValue(new Error('Hub unavailable'))
      const list = new LocalProjectList(() => fake.hub, vi.fn())
      list.start()
      await vi.advanceTimersByTimeAsync(0)
      let attempts = 1
      for (const delay of [2_000, 5_000, 15_000, 30_000, 30_000]) {
        await vi.advanceTimersByTimeAsync(delay - 1)
        expect(fake.hub.listProjects).toHaveBeenCalledTimes(attempts)
        await vi.advanceTimersByTimeAsync(1)
        expect(fake.hub.listProjects).toHaveBeenCalledTimes(++attempts)
      }
      expect(fake.hub.subscribeEvents).toHaveBeenCalledOnce()
      list.stop()
      await vi.advanceTimersByTimeAsync(60_000)
      expect(fake.hub.listProjects).toHaveBeenCalledTimes(attempts)
      expect(vi.getTimerCount()).toBe(0)
    })

    it('accepts an empty snapshot and cancels a failed-fetch retry after explicit refresh succeeds', async () => {
      const fake = fakeHub([A, B])
      const list = new LocalProjectList(() => fake.hub, vi.fn())
      list.start()
      await vi.advanceTimersByTimeAsync(0)
      fake.hub.listProjects.mockRejectedValueOnce(new Error('Hub unavailable'))
      await expect(list.refresh()).resolves.toBe(false)
      expect(paths(list)).toEqual(['/work/a', '/work/b'])
      expect(vi.getTimerCount()).toBe(1)

      fake.setProjects([])
      await expect(list.refresh()).resolves.toBe(true)
      expect(paths(list)).toEqual([])
      expect(vi.getTimerCount()).toBe(0)
      await vi.advanceTimersByTimeAsync(60_000)
      expect(fake.hub.listProjects).toHaveBeenCalledTimes(3)
      list.stop()
    })

    it('does not schedule background retries when the list was never started', async () => {
      const fake = fakeHub([])
      fake.hub.listProjects.mockRejectedValue(new Error('Hub unavailable'))
      const list = new LocalProjectList(() => fake.hub, vi.fn())
      await expect(list.refresh()).resolves.toBe(false)
      await vi.advanceTimersByTimeAsync(60_000)
      expect(fake.hub.listProjects).toHaveBeenCalledOnce()
      expect(vi.getTimerCount()).toBe(0)
    })

    it('re-reads when project events arrive during a snapshot instead of losing invalidations', async () => {
      const fake = fakeHub([A])
      const list = new LocalProjectList(() => fake.hub, vi.fn())
      list.start()
      await vi.advanceTimersByTimeAsync(0)
      const pending = deferred<HubProject[]>()
      fake.hub.listProjects.mockImplementationOnce(() => pending.promise)
      const refreshing = list.refresh()
      expect(list.refresh()).toBe(refreshing)
      expect(list.refresh()).toBe(refreshing)
      fake.setProjects([A, B, C])
      fake.emit('projects.changed')
      fake.emit('projects.changed')
      expect(fake.hub.listProjects).toHaveBeenCalledTimes(2)
      pending.resolve([A])
      await refreshing
      expect(paths(list)).toEqual(['/work/a', '/work/b', '/work/c'])
      expect(fake.hub.listProjects).toHaveBeenCalledTimes(3)
      list.stop()
    })

    it('ignores an old in-flight lifecycle without disturbing a restarted watcher', async () => {
      const fake = fakeHub([])
      const oldSnapshot = deferred<HubProject[]>()
      const newSnapshot = deferred<HubProject[]>()
      fake.hub.listProjects.mockImplementationOnce(() => oldSnapshot.promise)
        .mockImplementationOnce(() => newSnapshot.promise)
      const onChanged = vi.fn()
      const list = new LocalProjectList(() => fake.hub, onChanged)
      list.start()
      list.start()
      expect(fake.hub.listProjects).toHaveBeenCalledOnce()
      list.stop()
      list.stop()
      list.start()
      expect(fake.hub.listProjects).toHaveBeenCalledTimes(2)
      oldSnapshot.resolve([A])
      await vi.advanceTimersByTimeAsync(0)
      expect(paths(list)).toEqual([])
      expect(onChanged).not.toHaveBeenCalled()
      expect(fake.hub.subscribeEvents).not.toHaveBeenCalled()

      newSnapshot.resolve([B])
      await vi.advanceTimersByTimeAsync(0)
      expect(paths(list)).toEqual(['/work/b'])
      expect(onChanged).toHaveBeenCalledOnce()
      expect(fake.hub.subscribeEvents).toHaveBeenCalledOnce()
      const [oldCallback, signal] = fake.hub.subscribeEvents.mock.calls[0]
      list.stop()
      expect(signal.aborted).toBe(true)
      oldCallback({ kind: 'projects.changed', at: new Date().toISOString() })
      await vi.advanceTimersByTimeAsync(60_000)
      expect(fake.hub.listProjects).toHaveBeenCalledTimes(2)
      expect(vi.getTimerCount()).toBe(0)
    })

    it('suppresses requests, publication and retries while the application gate is inactive', async () => {
      let active = false
      const fake = fakeHub([A])
      const onChanged = vi.fn()
      const list = new LocalProjectList(() => fake.hub, onChanged, { isActive: () => active })
      list.start()
      await expect(list.refresh()).resolves.toBe(false)
      expect(fake.hub.listProjects).not.toHaveBeenCalled()
      active = true
      const pending = deferred<HubProject[]>()
      fake.hub.listProjects.mockImplementationOnce(() => pending.promise)
      list.start()
      active = false
      pending.resolve([A])
      await vi.advanceTimersByTimeAsync(60_000)
      expect(paths(list)).toEqual([])
      expect(onChanged).not.toHaveBeenCalled()
      expect(fake.hub.subscribeEvents).not.toHaveBeenCalled()
      expect(vi.getTimerCount()).toBe(0)
      active = true
      list.start()
      await vi.advanceTimersByTimeAsync(0)
      expect(paths(list)).toEqual(['/work/a'])
      fake.hub.listProjects.mockRejectedValueOnce(new Error('Hub unavailable'))
      await list.refresh()
      expect(vi.getTimerCount()).toBe(1)
      const attempts = fake.hub.listProjects.mock.calls.length
      active = false
      await vi.advanceTimersByTimeAsync(60_000)
      expect(fake.hub.listProjects).toHaveBeenCalledTimes(attempts)
      expect(vi.getTimerCount()).toBe(0)
      list.stop()
    })
  })
})
