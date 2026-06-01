import { describe, expect, it } from 'vitest'

import {
  normalizeReleaseTagVersion,
  resolveUpdateFromRelease,
  selectUpdateAsset,
  type GitHubReleaseAsset
} from './appUpdate'

const releaseAssets: GitHubReleaseAsset[] = [
  {
    name: 'DotCraft-v0.1.8-win-x64.zip',
    size: 10,
    browser_download_url: 'https://github.com/DotHarness/dotcraft/releases/download/v0.1.8/DotCraft-v0.1.8-win-x64.zip'
  },
  {
    name: 'DotCraft-v0.1.8-win-x64-Setup.exe',
    size: 20,
    browser_download_url:
      'https://github.com/DotHarness/dotcraft/releases/download/v0.1.8/DotCraft-v0.1.8-win-x64-Setup.exe'
  },
  {
    name: 'DotCraft-v0.1.8-win-arm64-Setup.exe',
    size: 25,
    browser_download_url:
      'https://github.com/DotHarness/dotcraft/releases/download/v0.1.8/DotCraft-v0.1.8-win-arm64-Setup.exe'
  },
  {
    name: 'DotCraft-v0.1.8-macos-x64.dmg',
    size: 30,
    browser_download_url:
      'https://github.com/DotHarness/dotcraft/releases/download/v0.1.8/DotCraft-v0.1.8-macos-x64.dmg'
  }
]

describe('app update release resolution', () => {
  it('normalizes v-prefixed release tags', () => {
    expect(normalizeReleaseTagVersion('v0.1.8')).toBe('0.1.8')
    expect(normalizeReleaseTagVersion('0.1.8')).toBe('0.1.8')
    expect(normalizeReleaseTagVersion('latest')).toBeNull()
  })

  it('selects the Windows installer instead of CLI archives', () => {
    expect(selectUpdateAsset(releaseAssets, 'win32', 'x64')?.name)
      .toBe('DotCraft-v0.1.8-win-x64-Setup.exe')
  })

  it('selects the Windows ARM64 installer for ARM64 builds', () => {
    expect(selectUpdateAsset(releaseAssets, 'win32', 'arm64')?.name)
      .toBe('DotCraft-v0.1.8-win-arm64-Setup.exe')
  })

  it('does not select a different architecture installer', () => {
    const x64OnlyAssets = releaseAssets.filter((asset) => !asset.name?.includes('arm64'))
    expect(selectUpdateAsset(x64OnlyAssets, 'win32', 'arm64')).toBeNull()
  })

  it('selects the macOS DMG for mac builds', () => {
    expect(selectUpdateAsset(releaseAssets, 'darwin', 'x64')?.name)
      .toBe('DotCraft-v0.1.8-macos-x64.dmg')
  })

  it('ignores non-DotHarness release download URLs', () => {
    const update = resolveUpdateFromRelease('0.1.7', {
      tag_name: 'v0.1.8',
      assets: [
        {
          name: 'DotCraft-v0.1.8-win-x64-Setup.exe',
          size: 20,
          browser_download_url: 'https://example.com/DotCraft-v0.1.8-win-x64-Setup.exe'
        }
      ]
    }, 'win32', 'x64')

    expect(update).toBeNull()
  })

  it('returns update metadata only when the release is newer', () => {
    expect(resolveUpdateFromRelease('0.1.8', { tag_name: 'v0.1.8', assets: releaseAssets }, 'win32', 'x64'))
      .toBeNull()
    expect(resolveUpdateFromRelease('0.1.7', {
      tag_name: 'v0.1.8',
      name: 'DotCraft 0.1.8',
      body: 'Release notes',
      assets: releaseAssets
    }, 'win32', 'x64')).toMatchObject({
      latestVersion: '0.1.8',
      releaseName: 'DotCraft 0.1.8',
      releaseNotes: 'Release notes',
      assetName: 'DotCraft-v0.1.8-win-x64-Setup.exe'
    })
  })
})
