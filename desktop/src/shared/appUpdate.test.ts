import { describe, expect, it } from 'vitest'

import {
  normalizeReleaseTagVersion,
  resolveUpdateFromRelease,
  type GitHubReleaseAsset
} from './appUpdate'

const tagName = 'v0.7.2'
const releaseBase = `https://github.com/DotHarness/dotcraft/releases/download/${tagName}/`

function asset(name: string, size: number): GitHubReleaseAsset {
  return {
    name,
    size,
    browser_download_url: `${releaseBase}${name}`
  }
}

const releaseAssets: GitHubReleaseAsset[] = [
  asset('DotCraft-Satellite-v0.7.2-win-x64-Installer.exe', 10),
  asset('DotCraft-Satellite-v0.7.2-win-x64-Setup.exe', 11),
  asset('DotCraft-v0.7.2-win-x64.zip', 12),
  asset('DotCraft-v0.7.2-win-x64-Setup.exe', 20),
  asset('DotCraft-v0.7.2-win-arm64-Setup.exe', 25),
  asset('DotCraft-v0.7.2-macos-x64.dmg', 30),
  asset('DotCraft-v0.7.2-macos-arm64.dmg', 35),
  asset('DotCraft-v0.7.2-linux-x64.deb', 40),
  asset('DotCraft-v0.7.2-linux-x64.AppImage', 45)
]

describe('app update release resolution', () => {
  it('normalizes v-prefixed release tags', () => {
    expect(normalizeReleaseTagVersion('v0.1.8')).toBe('0.1.8')
    expect(normalizeReleaseTagVersion('0.1.8')).toBeNull()
    expect(normalizeReleaseTagVersion('V0.1.8')).toBeNull()
    expect(normalizeReleaseTagVersion('latest')).toBeNull()
  })

  it('selects the exact Windows Desktop installer regardless of asset order', () => {
    for (const assets of [releaseAssets, [...releaseAssets].reverse()]) {
      expect(resolve(assets, 'win32', 'x64')?.assetName)
        .toBe('DotCraft-v0.7.2-win-x64-Setup.exe')
    }
  })

  it('selects the Windows ARM64 installer for ARM64 builds', () => {
    expect(resolve(releaseAssets, 'win32', 'arm64')?.assetName)
      .toBe('DotCraft-v0.7.2-win-arm64-Setup.exe')
  })

  it('selects the exact macOS package for each architecture', () => {
    expect(resolve(releaseAssets, 'darwin', 'x64')?.assetName)
      .toBe('DotCraft-v0.7.2-macos-x64.dmg')
    expect(resolve(releaseAssets, 'darwin', 'arm64')?.assetName)
      .toBe('DotCraft-v0.7.2-macos-arm64.dmg')
  })

  it('uses the declared Linux package priority instead of release order', () => {
    expect(resolve(releaseAssets, 'linux', 'x64')?.assetName)
      .toBe('DotCraft-v0.7.2-linux-x64.AppImage')
  })

  it('rejects releases without the exact version and architecture asset', () => {
    const wrongVersion = releaseAssets.filter((entry) => entry.name !== 'DotCraft-v0.7.2-win-x64-Setup.exe')
    wrongVersion.push(asset('DotCraft-v0.7.1-win-x64-Setup.exe', 20))

    expect(() => resolve(wrongVersion, 'win32', 'x64')).toThrow(/does not contain a DotCraft Desktop asset/)
    expect(() => resolve(releaseAssets, 'win32', 'ia32')).toThrow(/does not contain a DotCraft Desktop asset/)
  })

  it('rejects ambiguous exact assets', () => {
    const duplicate = asset('DotCraft-v0.7.2-win-x64-Setup.exe', 21)
    expect(() => resolve([...releaseAssets, duplicate], 'win32', 'x64'))
      .toThrow(/contains multiple/)
  })

  it('rejects an exact asset whose URL does not match its release path', () => {
    const assets = releaseAssets.map((entry) => entry.name === 'DotCraft-v0.7.2-win-x64-Setup.exe'
      ? { ...entry, browser_download_url: 'https://example.com/DotCraft-v0.7.2-win-x64-Setup.exe' }
      : entry)
    expect(() => resolve(assets, 'win32', 'x64')).toThrow(/invalid download URL/)
  })

  it('returns update metadata only when the release is newer', () => {
    expect(resolveUpdateFromRelease('0.7.2', { tag_name: tagName, assets: releaseAssets }, 'win32', 'x64'))
      .toBeNull()
    expect(resolveUpdateFromRelease('0.7.1', {
      tag_name: tagName,
      name: 'DotCraft 0.7.2',
      body: 'Release notes',
      assets: releaseAssets
    }, 'win32', 'x64')).toMatchObject({
      latestVersion: '0.7.2',
      releaseName: 'DotCraft 0.7.2',
      releaseNotes: 'Release notes',
      assetName: 'DotCraft-v0.7.2-win-x64-Setup.exe'
    })
  })

  it('ignores draft, prerelease, and non-newer releases before validating assets', () => {
    expect(resolveUpdateFromRelease('0.7.1', { tag_name: tagName, draft: true }, 'win32', 'x64')).toBeNull()
    expect(resolveUpdateFromRelease('0.7.1', { tag_name: tagName, prerelease: true }, 'win32', 'x64')).toBeNull()
    expect(resolveUpdateFromRelease('0.7.2', { tag_name: tagName }, 'win32', 'x64')).toBeNull()
  })
})

function resolve(
  assets: GitHubReleaseAsset[],
  platform: 'win32' | 'darwin' | 'linux',
  arch: string
) {
  return resolveUpdateFromRelease('0.7.1', { tag_name: tagName, assets }, platform, arch)
}
