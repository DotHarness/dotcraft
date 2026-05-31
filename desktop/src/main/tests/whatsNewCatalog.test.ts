import { mkdtemp, rm, mkdir, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  app: {
    isPackaged: false
  }
}))

import { WhatsNewCatalog } from '../whatsNewCatalog'

const tempDirs: string[] = []

afterEach(async () => {
  for (const dir of tempDirs.splice(0)) {
    await rm(dir, { recursive: true, force: true })
  }
  vi.restoreAllMocks()
})

async function createReleasesDir(): Promise<string> {
  const root = await mkdtemp(join(tmpdir(), 'dotcraft-whats-new-catalog-'))
  tempDirs.push(root)
  const releasesDir = join(root, 'releases')
  await mkdir(releasesDir)
  return releasesDir
}

function makeRelease(version: string): Record<string, unknown> {
  return {
    version,
    cards: [
      {
        id: 'demo',
        icon: 'message',
        title: {
          en: 'Demo',
          'zh-Hans': 'Demo'
        },
        summary: {
          en: 'Demo summary.',
          'zh-Hans': 'Demo summary.'
        }
      }
    ]
  }
}

async function writeJson(releasesDir: string, fileName: string, value: unknown): Promise<void> {
  await writeFile(join(releasesDir, fileName), JSON.stringify(value, null, 2), 'utf8')
}

describe('WhatsNewCatalog', () => {
  it('loads JSON release files and sorts them newest first', async () => {
    const releasesDir = await createReleasesDir()
    await writeJson(releasesDir, '0.1.6.json', makeRelease('0.1.6'))
    await writeJson(releasesDir, '0.1.10.json', makeRelease('0.1.10'))

    const catalog = new WhatsNewCatalog({ releasesDir })

    await expect(catalog.getReleases()).resolves.toMatchObject([
      { version: '0.1.10' },
      { version: '0.1.6' }
    ])
  })

  it('skips unreadable or invalid release configs safely', async () => {
    const releasesDir = await createReleasesDir()
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    await writeJson(releasesDir, 'valid.json', makeRelease('0.1.6'))
    await writeFile(join(releasesDir, 'broken.json'), '{', 'utf8')
    await writeJson(releasesDir, 'invalid.json', { version: 'latest', cards: [] })

    const catalog = new WhatsNewCatalog({ releasesDir })

    await expect(catalog.getReleases()).resolves.toMatchObject([{ version: '0.1.6' }])
    expect(warn).toHaveBeenCalled()
  })

  it('allows release text to omit non-English locales', async () => {
    const releasesDir = await createReleasesDir()
    await writeJson(releasesDir, '0.1.6.json', {
      version: '0.1.6',
      cards: [
        {
          id: 'demo',
          icon: 'message',
          title: { en: 'Demo' },
          summary: { en: 'Demo summary.' }
        }
      ]
    })

    const catalog = new WhatsNewCatalog({ releasesDir })

    await expect(catalog.getReleases()).resolves.toMatchObject([
      {
        version: '0.1.6',
        cards: [
          {
            title: { en: 'Demo' },
            summary: { en: 'Demo summary.' }
          }
        ]
      }
    ])
  })
})
