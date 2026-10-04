import { describe, expect, it, vi } from 'vitest'
import { DesktopHubError, type HubEvent, type HubProject } from '../desktopHub'
import { LocalProjectList } from '../localProjects'

function project(path: string, lastOpenedAt = '2026-10-04T08:00:00Z'): HubProject {
  return { path, displayName: path.split('/').pop() ?? path, lastOpenedAt, running: false }
}

function fakeHub(initial: HubProject[]) {
  let projects = initial
  let emit: ((event: HubEvent) => void) | null = null
  const hub = {
    listProjects: vi.fn(async () => projects),
    openProject: vi.fn(async (path: string) => {
      const opened = project(path, '2026-10-04T09:00:00Z')
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

describe('LocalProjectList', () => {
  it('re-reads Hub projects on projects.changed and notifies', async () => {
    const fake = fakeHub([project('/work/b'), project('/work/a')])
    const onChanged = vi.fn()
    const list = new LocalProjectList(() => fake.hub, onChanged)

    list.start()
    await vi.waitFor(() => expect(fake.hub.subscribeEvents).toHaveBeenCalled())
    expect(paths(list)).toEqual(['/work/a', '/work/b'])

    fake.setProjects([project('/work/c'), project('/work/b'), project('/work/a')])
    fake.emit('appserver.running')
    fake.emit('projects.changed')

    await vi.waitFor(() => expect(paths(list)).toEqual(['/work/a', '/work/b', '/work/c']))
    expect(fake.hub.listProjects).toHaveBeenCalledTimes(2)
    expect(onChanged).toHaveBeenCalledTimes(2)
    list.stop()
  })

  it('opens and removes through Hub, keeping positions of known projects', async () => {
    const fake = fakeHub([project('/work/b'), project('/work/a')])
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

  it('shows no projects when Hub is unreachable', async () => {
    const unreachable = fakeHub([project('/work/a')])
    unreachable.hub.listProjects.mockRejectedValue(new DesktopHubError('hubUnavailable', 'DotCraft Hub could not be started.'))
    const list = new LocalProjectList(() => unreachable.hub, vi.fn())
    await list.refresh()
    expect(list.list()).toEqual([])
  })
})
