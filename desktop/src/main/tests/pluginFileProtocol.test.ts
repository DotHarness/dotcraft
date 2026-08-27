import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'fs'
import { tmpdir } from 'os'
import { dirname, join } from 'path'
import { fileURLToPath } from 'url'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const { protocolRegisterSchemesAsPrivilegedMock, netFetchMock } = vi.hoisted(() => ({
  protocolRegisterSchemesAsPrivilegedMock: vi.fn(),
  netFetchMock: vi.fn()
}))

vi.mock('electron', () => ({
  protocol: {
    registerSchemesAsPrivileged: protocolRegisterSchemesAsPrivilegedMock,
    handle: vi.fn()
  },
  net: { fetch: netFetchMock }
}))

import {
  PLUGIN_FILE_SCHEME,
  buildPluginFileUrl,
  clearDesktopPluginModuleRoutes,
  computeDesktopPluginRevision,
  handlePluginFileRequest,
  pluginUrlToRoute,
  registerDesktopPluginModuleRoute,
  registerPluginFileScheme,
  removeDesktopPluginModuleRoute
} from '../pluginFileProtocol'

interface RevisionFixture {
  entry: string
  styles: string[]
  files: Array<{ path: string; content: string }>
  expectedRevision: string
}

const fixturePath = fileURLToPath(new URL('../../../../tests/fixtures/desktop-plugin-revision.json', import.meta.url))
const revisionFixture = JSON.parse(readFileSync(fixturePath, 'utf8')) as RevisionFixture
const tempDirs: string[] = []

beforeEach(() => {
  clearDesktopPluginModuleRoutes()
  protocolRegisterSchemesAsPrivilegedMock.mockReset()
  netFetchMock.mockReset()
  netFetchMock.mockResolvedValue(new Response('export {}', {
    status: 200,
    headers: { 'X-Upstream': 'ok' }
  }))
})

afterEach(() => {
  clearDesktopPluginModuleRoutes()
  for (const dir of tempDirs.splice(0)) rmSync(dir, { recursive: true, force: true })
})

function createPluginRoot(pluginId = 'fixture.desktop', version = '1.0.0'): string {
  const root = mkdtempSync(join(tmpdir(), 'desktop-plugin-protocol-'))
  tempDirs.push(root)
  writePluginRoot(root, pluginId, version)
  return root
}

function writePluginRoot(root: string, pluginId = 'fixture.desktop', version = '1.0.0'): void {
  for (const file of revisionFixture.files) {
    const output = join(root, 'desktop', 'dist', ...file.path.split('/'))
    mkdirSync(dirname(output), { recursive: true })
    writeFileSync(output, file.content, 'utf8')
  }
  const manifestPath = join(root, '.craft-plugin', 'plugin.json')
  mkdirSync(dirname(manifestPath), { recursive: true })
  writeFileSync(manifestPath, JSON.stringify({
    id: pluginId,
    version,
    desktop: { entry: revisionFixture.entry, styles: revisionFixture.styles }
  }), 'utf8')
}

describe('Desktop Plugin module protocol', () => {
  it('registers dotcraft-plugin with CORS-enabled fetch support', () => {
    registerPluginFileScheme()
    expect(protocolRegisterSchemesAsPrivilegedMock).toHaveBeenCalledWith([{
      scheme: PLUGIN_FILE_SCHEME,
      privileges: {
        standard: true,
        secure: true,
        supportFetchAPI: true,
        bypassCSP: false,
        stream: true,
        corsEnabled: true
      }
    }])
  })

  it('matches the shared Core revision fixture including directory entries', async () => {
    const root = createPluginRoot()
    const revision = await computeDesktopPluginRevision(join(root, 'desktop', 'dist'), {
      entry: revisionFixture.entry,
      styles: revisionFixture.styles
    })
    expect(revision).toBe(revisionFixture.expectedRevision)
  })

  it('binds URLs to an exact plugin revision and only serves that generation', async () => {
    const root = createPluginRoot('Fixture.Desktop')
    const route = await registerDesktopPluginModuleRoute({
      pluginId: 'Fixture.Desktop',
      version: '1.0.0',
      revision: revisionFixture.expectedRevision,
      rootPath: root
    })

    expect(route.entryUrl).toBe(
      `${PLUGIN_FILE_SCHEME}://fixture.desktop/${revisionFixture.expectedRevision}/index.mjs`
    )
    expect(route.styleUrls).toEqual([
      `${PLUGIN_FILE_SCHEME}://fixture.desktop/${revisionFixture.expectedRevision}/styles/base.css`
    ])
    expect(pluginUrlToRoute(route.entryUrl)).toEqual({
      pluginId: 'fixture.desktop',
      revision: revisionFixture.expectedRevision,
      relativePath: 'index.mjs'
    })

    const response = await handlePluginFileRequest(new Request(route.entryUrl, {
      headers: { Origin: 'http://localhost:5173' }
    }))
    expect(response.status).toBe(200)
    expect(response.headers.get('Content-Type')).toBe('text/javascript; charset=utf-8')
    expect(response.headers.get('Access-Control-Allow-Origin')).toBe('http://localhost:5173')
    expect(netFetchMock).toHaveBeenCalledOnce()

    removeDesktopPluginModuleRoute('fixture.desktop', revisionFixture.expectedRevision)
    expect((await handlePluginFileRequest(new Request(route.entryUrl))).status).toBe(403)
  })

  it('keeps a local revision immutable after the live dist is replaced', async () => {
    const root = createPluginRoot()
    const liveDist = join(root, 'desktop', 'dist')
    const lazyRelativePath = 'chunks/lazy.mjs'
    const liveLazyPath = join(liveDist, ...lazyRelativePath.split('/'))
    mkdirSync(dirname(liveLazyPath), { recursive: true })
    writeFileSync(liveLazyPath, 'export const generation = "A"', 'utf8')
    const revision = await computeDesktopPluginRevision(liveDist, {
      entry: revisionFixture.entry,
      styles: revisionFixture.styles
    })
    const request = {
      pluginId: 'fixture.desktop',
      version: '1.0.0',
      revision,
      rootPath: root
    }
    await registerDesktopPluginModuleRoute(request)
    await registerDesktopPluginModuleRoute(request)

    rmSync(liveDist, { recursive: true, force: true })
    mkdirSync(liveDist, { recursive: true })
    writeFileSync(join(liveDist, 'index.mjs'), 'export const generation = "B"', 'utf8')
    expect(existsSync(liveLazyPath)).toBe(false)

    let servedPath = ''
    netFetchMock.mockImplementation(async (url: string) => {
      servedPath = fileURLToPath(url)
      return new Response(readFileSync(servedPath, 'utf8'))
    })
    const lazyUrl = buildPluginFileUrl('fixture.desktop', revision, lazyRelativePath)
    const response = await handlePluginFileRequest(new Request(lazyUrl))

    expect(response.status).toBe(200)
    expect(await response.text()).toBe('export const generation = "A"')
    expect(servedPath).not.toBe(liveLazyPath)
    expect(existsSync(servedPath)).toBe(true)
    const snapshotRoot = dirname(dirname(servedPath))

    removeDesktopPluginModuleRoute('fixture.desktop', revision)
    await vi.waitFor(() => expect(existsSync(snapshotRoot)).toBe(false))
    expect((await handlePluginFileRequest(new Request(lazyUrl))).status).toBe(403)
  })

  it('rejects a snapshot whose revision does not match local code', async () => {
    await expect(registerDesktopPluginModuleRoute({
      pluginId: 'fixture.desktop',
      version: '1.0.0',
      revision: '0'.repeat(64),
      rootPath: createPluginRoot()
    })).rejects.toThrow('revision does not match')
  })

  it.each(['.', '..', ':x'])('rejects invalid plugin id %s before route lookup', async (pluginId) => {
    await expect(registerDesktopPluginModuleRoute({
      pluginId,
      version: '1.0.0',
      revision: revisionFixture.expectedRevision,
      rootPath: createPluginRoot()
    })).rejects.toThrow('id is invalid')
  })

  it('ignores a remote-supplied root and requires an exact packaged plugin', async () => {
    const packagedRoot = mkdtempSync(join(tmpdir(), 'desktop-plugin-packaged-'))
    tempDirs.push(packagedRoot)
    writePluginRoot(join(packagedRoot, 'fixture.desktop'))

    await expect(registerDesktopPluginModuleRoute({
      pluginId: 'fixture.desktop',
      version: '1.0.0',
      revision: revisionFixture.expectedRevision,
      rootPath: 'Z:\\untrusted\\remote-path'
    }, {
      remote: true,
      packagedPluginRoots: [packagedRoot]
    })).resolves.toMatchObject({
      entryUrl: `${PLUGIN_FILE_SCHEME}://fixture.desktop/${revisionFixture.expectedRevision}/index.mjs`
    })

    await expect(registerDesktopPluginModuleRoute({
      pluginId: 'missing.desktop',
      version: '1.0.0',
      revision: revisionFixture.expectedRevision,
      rootPath: createPluginRoot()
    }, {
      remote: true,
      packagedPluginRoots: [packagedRoot]
    })).rejects.toThrow('is not packaged')
  })

  it('rejects links inside desktop/dist instead of serving their targets', async () => {
    const root = createPluginRoot()
    const outside = mkdtempSync(join(tmpdir(), 'desktop-plugin-outside-'))
    tempDirs.push(outside)
    writeFileSync(join(outside, 'outside.mjs'), 'export {}', 'utf8')
    symlinkSync(
      outside,
      join(root, 'desktop', 'dist', 'linked'),
      process.platform === 'win32' ? 'junction' : 'dir'
    )

    await expect(registerDesktopPluginModuleRoute({
      pluginId: 'fixture.desktop',
      version: '1.0.0',
      revision: revisionFixture.expectedRevision,
      rootPath: root
    })).rejects.toThrow('filesystem links')
  })

  it('rejects manifest paths that traverse out of desktop/dist', async () => {
    const root = createPluginRoot()
    writeFileSync(join(root, '.craft-plugin', 'plugin.json'), JSON.stringify({
      id: 'fixture.desktop',
      version: '1.0.0',
      desktop: { entry: './desktop/dist/../outside.mjs', styles: [] }
    }), 'utf8')

    await expect(registerDesktopPluginModuleRoute({
      pluginId: 'fixture.desktop',
      version: '1.0.0',
      revision: revisionFixture.expectedRevision,
      rootPath: root
    })).rejects.toThrow('invalid segment')
  })

  it('rejects traversal paths before route lookup', () => {
    const url = buildPluginFileUrl('fixture.desktop', revisionFixture.expectedRevision, 'index.mjs')
    expect(url).not.toContain('desktop/dist')
    expect(() => pluginUrlToRoute(url.replace('/index.mjs', '/%2e%2e/index.mjs'))).toThrow()
  })
})
