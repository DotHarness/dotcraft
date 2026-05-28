import { app } from 'electron'
import { existsSync } from 'fs'
import { readdir, readFile } from 'fs/promises'
import { join } from 'path'
import {
  parseWhatsNewRelease,
  sortWhatsNewReleasesNewestFirst,
  type WhatsNewRelease
} from '../shared/whatsNew'

export function defaultWhatsNewReleasesDir(): string {
  const packaged = join(process.resourcesPath, 'whats-new', 'releases')
  const dev = join(__dirname, '../../resources/whats-new/releases')
  return app.isPackaged ? packaged : dev
}

interface WhatsNewCatalogOptions {
  releasesDir?: string
}

export class WhatsNewCatalog {
  private readonly releasesDir: string
  private cachedReleases: Promise<WhatsNewRelease[]> | null = null

  constructor(options: WhatsNewCatalogOptions = {}) {
    this.releasesDir = options.releasesDir ?? defaultWhatsNewReleasesDir()
  }

  getReleases(): Promise<WhatsNewRelease[]> {
    this.cachedReleases ??= this.loadReleases()
    return this.cachedReleases
  }

  private async loadReleases(): Promise<WhatsNewRelease[]> {
    if (!existsSync(this.releasesDir)) {
      console.warn('[desktop] whats-new releases directory not found', this.releasesDir)
      return []
    }

    let fileNames: string[]
    try {
      fileNames = await readdir(this.releasesDir)
    } catch (error) {
      console.warn('[desktop] failed to list whats-new releases', error)
      return []
    }

    const releases: WhatsNewRelease[] = []
    for (const fileName of fileNames.filter((entry) => entry.toLowerCase().endsWith('.json'))) {
      const release = await this.readRelease(fileName)
      if (release) releases.push(release)
    }
    return sortWhatsNewReleasesNewestFirst(releases)
  }

  private async readRelease(fileName: string): Promise<WhatsNewRelease | null> {
    const path = join(this.releasesDir, fileName)
    try {
      const raw = await readFile(path, 'utf8')
      const release = parseWhatsNewRelease(JSON.parse(raw))
      if (!release) {
        console.warn('[desktop] skipped invalid whats-new release config', path)
      }
      return release
    } catch (error) {
      console.warn('[desktop] skipped unreadable whats-new release config', path, error)
      return null
    }
  }
}
