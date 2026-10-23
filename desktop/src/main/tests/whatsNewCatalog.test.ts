import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  app: {
    isPackaged: false
  }
}))

import { WhatsNewCatalog } from '../whatsNewCatalog'

let releasesDir: string | null = null

afterEach(async () => {
  if (releasesDir) await rm(releasesDir, { recursive: true, force: true })
  releasesDir = null
  vi.restoreAllMocks()
})

function makeRelease(version: string, title: Record<string, string>): Record<string, unknown> {
  return { version, cards: [{ id: 'demo', title, summary: title }] }
}

it('loads valid release files newest first, skips broken ones, and allows English-only text', async () => {
  const dir = releasesDir = await mkdtemp(join(tmpdir(), 'dotcraft-whats-new-catalog-'))
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
  await writeFile(join(dir, '0.1.6.json'), JSON.stringify(makeRelease('0.1.6', { en: 'Demo' })), 'utf8')
  await writeFile(join(dir, '0.1.10.json'), JSON.stringify(makeRelease('0.1.10', { en: 'Demo', 'zh-Hans': 'Demo' })), 'utf8')
  await writeFile(join(dir, 'broken.json'), '{', 'utf8')
  await writeFile(join(dir, 'invalid.json'), JSON.stringify({ version: 'latest', cards: [] }), 'utf8')

  const releases = await new WhatsNewCatalog({ releasesDir: dir }).getReleases()

  expect(releases).toMatchObject([
    { version: '0.1.10' },
    { version: '0.1.6', cards: [{ title: { en: 'Demo' }, summary: { en: 'Demo' } }] }
  ])
  expect(releases).toHaveLength(2)
  expect(warn).toHaveBeenCalled()
})
