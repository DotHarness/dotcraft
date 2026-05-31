import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
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

import { loadSettings } from '../settings'

describe('desktop settings locale defaults', () => {
  let tempRoot = ''

  afterEach(async () => {
    if (tempRoot) {
      await rm(tempRoot, { recursive: true, force: true })
      tempRoot = ''
    }
    vi.clearAllMocks()
  })

  async function useTempUserData(systemLocale: string): Promise<string> {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-settings-locale-'))
    electronMock.getPath.mockReturnValue(tempRoot)
    electronMock.getLocale.mockReturnValue(systemLocale)
    return tempRoot
  }

  it('uses the system locale when settings have no saved locale yet', async () => {
    await useTempUserData('ja-JP')

    expect(loadSettings().locale).toBe('ja')
  })

  it('normalizes a saved locale before returning settings', async () => {
    const root = await useTempUserData('en-US')
    await writeFile(join(root, 'settings.json'), JSON.stringify({ locale: 'fr-CA' }), 'utf8')

    expect(loadSettings().locale).toBe('fr')
  })
})
