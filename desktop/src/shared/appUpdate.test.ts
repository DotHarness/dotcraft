import { describe, expect, it } from 'vitest'

import {
  resolveUpdateFromRelease,
  type GitHubReleaseAsset
} from './appUpdate'

const tagName = 'v0.1.8'
const releaseBase = `https://github.com/DotHarness/dotcraft/releases/download/${tagName}/`

function asset(name: string, size: number): GitHubReleaseAsset {
  return {
    name,
    size,
    browser_download_url: `${releaseBase}${name}`
  }
}

const releaseAssets: GitHubReleaseAsset[] = [
  asset('DotCraft-Satellite-v0.1.8-win-x64-Setup.exe', 10),
  asset('DotCraft-v0.1.8-win-x64.zip', 12),
  asset('DotCraft-v0.1.8-win-x64-Setup.exe', 20),
  asset('DotCraft-v0.1.8-win-arm64-Setup.exe', 25),
  asset('DotCraft-v0.1.8-macos-x64.dmg', 30),
  asset('DotCraft-v0.1.8-macos-arm64.dmg', 35)
]

describe('app update release resolution', () => {
  it.each([
    ['win32', 'x64', 'DotCraft-v0.1.8-win-x64-Setup.exe'],
    ['win32', 'arm64', 'DotCraft-v0.1.8-win-arm64-Setup.exe'],
    ['darwin', 'x64', 'DotCraft-v0.1.8-macos-x64.dmg'],
    ['darwin', 'arm64', 'DotCraft-v0.1.8-macos-arm64.dmg']
  ] as const)('selects the exact %s/%s Desktop asset', (platform, arch, expectedName) => {
    expect(resolve(releaseAssets, platform, arch)?.assetName).toBe(expectedName)
  })

  it('rejects a different architecture installer', () => {
    const x64OnlyAssets = releaseAssets.filter((entry) => !entry.name?.includes('arm64'))
    expect(() => resolve(x64OnlyAssets, 'win32', 'arm64'))
      .toThrow(/does not contain a DotCraft Desktop asset/)
  })

  it('rejects an exact asset whose URL does not match its release path', () => {
    const assets = releaseAssets.map((entry) => entry.name === 'DotCraft-v0.1.8-win-x64-Setup.exe'
      ? { ...entry, browser_download_url: 'https://example.com/DotCraft-v0.1.8-win-x64-Setup.exe' }
      : entry)
    expect(() => resolve(assets, 'win32', 'x64')).toThrow(/invalid download URL/)
  })

  it('returns an update only when the release is newer', () => {
    expect(resolveUpdateFromRelease('0.1.8', { tag_name: tagName, assets: releaseAssets }, 'win32', 'x64'))
      .toBeNull()
    expect(resolveUpdateFromRelease('0.1.7', {
      tag_name: tagName,
      assets: releaseAssets
    }, 'win32', 'x64')).toMatchObject({
      latestVersion: '0.1.8',
      assetName: 'DotCraft-v0.1.8-win-x64-Setup.exe'
    })
  })
})

function resolve(
  assets: GitHubReleaseAsset[],
  platform: 'win32' | 'darwin' | 'linux',
  arch: string
) {
  return resolveUpdateFromRelease('0.1.7', { tag_name: tagName, assets }, platform, arch)
}
