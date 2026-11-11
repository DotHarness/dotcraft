import { describe, expect, it, vi } from 'vitest'
import type { HubProject } from '../desktopHub'
import { restoreRecentProjects, type RecentProjectRestoreHost } from '../recentProjectRestore'

function deferred() {
  let resolve!: () => void
  const promise = new Promise<void>((done) => { resolve = done })
  return { promise, resolve }
}

function fixture(foregroundPath = '/work/foreground') {
  const projects: HubProject[] = Array.from({ length: 7 }, (_, i) => ({
    path: `/work/${i}`, displayName: `${i}`, addedAt: '2026-11-01T00:00:00Z',
    lastOpenedAt: `2026-11-${String(i + 1).padStart(2, '0')}T00:00:00Z`, running: false
  }))
  const host: RecentProjectRestoreHost = {
    foregroundPath,
    isChatWorkspace: (path) => path === '/chats',
    isActive: () => true,
    projects: {
      refresh: vi.fn(async () => true),
      list: () => projects,
      has: (path) => projects.some((project) => project.path === path)
    },
    isEligible: () => true,
    startProject: vi.fn(async () => {}),
    setProjectState: vi.fn()
  }
  return { host, projects, start: vi.mocked(host.startProject), states: vi.mocked(host.setProjectState) }
}

describe('recent project startup restore', () => {
  it('reserves a slot for the foreground project and restores the three most recently opened projects', async () => {
    const { host, projects, start } = fixture('/work/2')
    const snapshot = structuredClone(projects)
    await restoreRecentProjects(host)
    expect(start.mock.calls.map(([path]) => path)).toEqual(['/work/6', '/work/5', '/work/4'])
    expect(projects).toEqual(snapshot)
  })

  it('restores four projects from Chats without starting Chats as a project', async () => {
    const { host, projects, start } = fixture('/chats')
    projects.push({ ...projects[6], path: '/chats', lastOpenedAt: '2026-11-11T00:00:00Z' })
    await restoreRecentProjects(host)
    expect(start.mock.calls.map(([path]) => path)).toEqual(['/work/6', '/work/5', '/work/4', '/work/3'])
  })

  it('skips missing or unconfigured projects before applying the limit', async () => {
    const { host, start } = fixture()
    host.isEligible = (path) => path !== '/work/6' && path !== '/work/5'
    await restoreRecentProjects(host)
    expect(start.mock.calls.map(([path]) => path)).toEqual(['/work/4', '/work/3', '/work/2'])
  })

  it('admits only two starts at a time and isolates failure without replenishing the restore limit', async () => {
    const { host, start, states } = fixture('/chats')
    const first = deferred()
    const second = deferred()
    const admitted = deferred()
    let active = 0
    let peak = 0
    start.mockImplementation(async (path) => {
      active += 1
      peak = Math.max(peak, active)
      if (path === '/work/6') await first.promise
      if (path === '/work/5') { admitted.resolve(); await second.promise }
      active -= 1
      if (path === '/work/6') throw new Error('start failed')
    })
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    try {
      const restoring = restoreRecentProjects(host)
      await admitted.promise
      expect(start).toHaveBeenCalledTimes(2)
      first.resolve()
      second.resolve()
      await restoring
      expect(peak).toBe(2)
      expect(start).toHaveBeenCalledTimes(4)
      expect(states).toHaveBeenCalledWith('/work/6', { state: 'connecting' })
      expect(states).toHaveBeenCalledWith('/work/6', { state: 'error', errorMessage: 'start failed' })
      expect(states).toHaveBeenCalledWith('/work/4', undefined)
    } finally { warn.mockRestore() }
  })

  it('does not start projects from a failed catalog snapshot', async () => {
    const { host, start } = fixture()
    vi.mocked(host.projects.refresh).mockResolvedValue(false)
    await restoreRecentProjects(host)
    expect(start).not.toHaveBeenCalled()
  })

  it('stops queued admission when the owner shuts down during in-flight starts', async () => {
    const { host, start } = fixture('/chats')
    const release = deferred()
    const admitted = deferred()
    let active = true
    host.isActive = () => active
    start.mockImplementation(async () => {
      if (start.mock.calls.length === 2) admitted.resolve()
      await release.promise
    })
    const restoring = restoreRecentProjects(host)
    await admitted.promise
    active = false
    release.resolve()
    await restoring
    expect(start).toHaveBeenCalledTimes(2)
  })

  it('skips a queued project removed after the restore snapshot', async () => {
    const { host, start, projects } = fixture('/chats')
    const release = deferred()
    const admitted = deferred()
    start.mockImplementation(async () => {
      if (start.mock.calls.length === 2) admitted.resolve()
      await release.promise
    })
    const restoring = restoreRecentProjects(host)
    await admitted.promise
    projects.splice(projects.findIndex((project) => project.path === '/work/4'), 1)
    release.resolve()
    await restoring
    expect(start.mock.calls.map(([path]) => path)).toEqual(['/work/6', '/work/5', '/work/3'])
  })
})
