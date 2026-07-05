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

  it('loads explicit collapsed sidebar section preferences', async () => {
    const root = await useTempUserData()
    await writeFile(join(root, 'settings.json'), JSON.stringify({
      projectsSectionCollapsed: true,
      chatsSectionCollapsed: true
    }), 'utf8')

    const settings = loadSettings()

    expect(settings.projectsSectionCollapsed).toBe(true)
    expect(settings.chatsSectionCollapsed).toBe(true)
  })

  it('treats false and invalid collapsed sidebar section values as expanded defaults on load', async () => {
    const root = await useTempUserData()
    await writeFile(join(root, 'settings.json'), JSON.stringify({
      projectsSectionCollapsed: false,
      chatsSectionCollapsed: 'true'
    }), 'utf8')

    const settings = loadSettings()

    expect(settings.projectsSectionCollapsed).toBeUndefined()
    expect(settings.chatsSectionCollapsed).toBeUndefined()
  })

  it('saves only explicit collapsed sidebar section preferences', async () => {
    await useTempUserData()

    saveSettings({
      projectsSectionCollapsed: true,
      chatsSectionCollapsed: true
    })

    const saved = await readSavedSettings()
    expect(saved.projectsSectionCollapsed).toBe(true)
    expect(saved.chatsSectionCollapsed).toBe(true)
  })

  it('omits expanded default sidebar section preferences when saving', async () => {
    await useTempUserData()

    saveSettings({
      projectsSectionCollapsed: false,
      chatsSectionCollapsed: 'yes' as unknown as boolean
    } satisfies AppSettings)

    const saved = await readSavedSettings()
    expect(saved).not.toHaveProperty('projectsSectionCollapsed')
    expect(saved).not.toHaveProperty('chatsSectionCollapsed')
  })
})
