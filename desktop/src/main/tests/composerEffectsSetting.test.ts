import { mkdtemp, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, it, vi } from 'vitest'
import { loadSettings, saveSettings } from '../settings'

const getPath = vi.hoisted(() => vi.fn())
vi.mock('electron', () => ({
  app: { getPath, getLocale: () => 'en-US' }
}))

it('persists Plain Composer effects across settings loads', async () => {
  const userData = await mkdtemp(join(tmpdir(), 'dotcraft-composer-effects-'))
  getPath.mockReturnValue(userData)
  try {
    saveSettings({ composerEffects: 'plain' })
    expect(loadSettings().composerEffects).toBe('plain')
  } finally {
    await rm(userData, { recursive: true, force: true })
  }
})
