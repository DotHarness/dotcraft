import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'

const electronMock = vi.hoisted(() => ({
  getPath: vi.fn(),
  getLocale: vi.fn()
}))

vi.mock('electron', () => ({
  app: {
    getPath: electronMock.getPath,
    getLocale: electronMock.getLocale
  }
}))

import { loadSettings, saveSettings, type AppSettings } from '../settings'

describe('desktop sidebar section settings', () => {
  let tempRoot = ''

  afterEach(async () => {
    if (tempRoot) {
      await rm(tempRoot, { recursive: true, force: true })
      tempRoot = ''
    }
    vi.clearAllMocks()
  })

  async function useTempUserData(): Promise<string> {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-sidebar-sections-'))
    electronMock.getPath.mockReturnValue(tempRoot)
    electronMock.getLocale.mockReturnValue('en-US')
    return tempRoot
  }

  async function readSavedSettings(): Promise<Record<string, unknown>> {
    const raw = await readFile(join(tempRoot, 'settings.json'), 'utf8')
    return JSON.parse(raw) as Record<string, unknown>
  }

  it('loads only explicit true collapsed sidebar section preferences', async () => {
    const root = await useTempUserData()
    await writeFile(join(root, 'settings.json'), JSON.stringify({
      projectsSectionCollapsed: true,
      pinnedSectionCollapsed: 'false',
      chatsSectionCollapsed: 'true'
    }), 'utf8')

    const settings = loadSettings()

    expect(settings.projectsSectionCollapsed).toBe(true)
    expect(settings.pinnedSectionCollapsed).toBeUndefined()
    expect(settings.chatsSectionCollapsed).toBeUndefined()
  })

  it('saves only explicit collapsed sidebar section preferences', async () => {
    await useTempUserData()

    saveSettings({
      projectsSectionCollapsed: true,
      pinnedSectionCollapsed: false,
      chatsSectionCollapsed: 'yes' as unknown as boolean
    } satisfies AppSettings)

    const saved = await readSavedSettings()
    expect(saved.projectsSectionCollapsed).toBe(true)
    expect(saved).not.toHaveProperty('pinnedSectionCollapsed')
    expect(saved).not.toHaveProperty('chatsSectionCollapsed')
  })

  it('saves thread ordering preferences, omitting defaults and emptied orders', async () => {
    await useTempUserData()

    saveSettings({
      recentsThreadSort: 'manual',
      projectsThreadSort: 'updated',
      recentsShowProjects: false,
      recentsThreadOrder: ['thread-b', 'thread-a', 'thread-b'],
      threadOrderByProject: {
        'C:\\Projects\\A': ['thread-c'],
        'C:\\Projects\\B': []
      }
    })

    const saved = await readSavedSettings()
    expect(saved.recentsThreadSort).toBe('manual')
    expect(saved).not.toHaveProperty('projectsThreadSort')
    expect(saved).not.toHaveProperty('recentsShowProjects')
    expect(saved.recentsThreadOrder).toEqual(['thread-b', 'thread-a'])
    expect(saved.threadOrderByProject).toEqual({ 'c:/projects/a': ['thread-c'] })
  })
})
