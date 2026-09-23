import { promises as fs } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { fileURLToPath } from 'url'
import { zipSync, strToU8 } from 'fflate'
import { afterEach, describe, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  protocol: { registerSchemesAsPrivileged: vi.fn(), handle: vi.fn() },
  net: { fetch: async (url: string) => new Response(await fs.readFile(fileURLToPath(url))) }
}))

import { DesktopPluginArtifactCache, desktopPluginSourceKey, extractDesktopArtifact } from '../desktopPluginArtifactCache'
import { DesktopPluginModules, type DesktopPluginConnection } from '../desktopPluginModules'
import { computeDesktopPluginRevision, handlePluginFileRequest, clearDesktopPluginModuleRoutes } from '../pluginFileProtocol'

const roots: string[] = []
afterEach(async () => {
  clearDesktopPluginModuleRoutes()
  for (const root of roots.splice(0)) await fs.rm(root, { recursive: true, force: true })
})

async function fixture() {
  const root = await fs.mkdtemp(join(tmpdir(), 'desktop-artifact-test-'))
  roots.push(root)
  const desktop = { entry: './desktop/dist/index.mjs', styles: ['./desktop/dist/theme.css'] }
  const files = {
    '.craft-plugin/': new Uint8Array(),
    '.craft-plugin/plugin.json': strToU8(JSON.stringify({ id: 'fixture.desktop', version: '1.0.0', desktop })),
    'desktop/': new Uint8Array(), 'desktop/dist/': new Uint8Array(), 'desktop/dist/empty/': new Uint8Array(),
    'desktop/dist/index.mjs': strToU8('export const remote = true'),
    'desktop/dist/theme.css': strToU8('body {color: red}'),
    'desktop/dist/lazy.mjs': strToU8('export const lazy = true')
  }
  const archive = Buffer.from(zipSync(files))
  const original = join(root, 'original')
  await extractDesktopArtifact(archive, original)
  const revision = await computeDesktopPluginRevision(join(original, 'desktop', 'dist'), desktop)
  const request = { pluginId: 'fixture.desktop', revision, version: '1.0.0', rootPath: '/remote/not-local' }
  const read = vi.fn(async ({ offset }: { offset: number }) => ({ totalBytes: archive.length, dataBase64: archive.subarray(offset, offset + 1024 * 1024).toString('base64') }))
  const source = desktopPluginSourceKey('host/stack', '/workspace')
  return { root, archive, files, request, read, source }
}

describe('remote Desktop artifacts', () => {
  it('validates, reuses and repairs the same revision cache without a local installation', async () => {
    const f = await fixture()
    const cache = new DesktopPluginArtifactCache(join(f.root, 'cache'))
    const bundle = await cache.prepare(f.source, f.request, f.read, () => true)
    expect(await fs.readFile(join(bundle, 'desktop/dist/lazy.mjs'), 'utf8')).toContain('lazy = true')
    await cache.prepare(f.source, { ...f.request, version: '2.0.0' }, f.read, () => true)
    expect(f.read).toHaveBeenCalledTimes(1)
    await fs.writeFile(join(bundle, 'desktop/dist/index.mjs'), 'broken')
    await cache.prepare(f.source, f.request, f.read, () => true)
    expect(f.read).toHaveBeenCalledTimes(2)
    expect(await fs.readFile(join(bundle, 'desktop/dist/index.mjs'), 'utf8')).toContain('remote = true')
  })

  it('does not publish a mismatched revision or a download whose source changed', async () => {
    const f = await fixture()
    const cache = new DesktopPluginArtifactCache(join(f.root, 'cache'))
    await expect(cache.prepare(f.source, { ...f.request, revision: 'f'.repeat(64) }, f.read, () => true)).rejects.toThrow('revision')
    let current = true
    const read = async (params: { id: string; revision: string; offset: number }) => { current = false; return f.read(params) }
    await expect(cache.prepare(f.source, f.request, read, () => current)).rejects.toThrow('source changed')
    expect(await fs.readdir(join(f.root, 'cache', f.source, f.request.pluginId))).toEqual([])
  })

  it.each(['../escape', 'desktop/dist/../../escape', 'desktop/dist/C:escape', 'desktop/dist/a\\b', 'lib/server.dll'])(
    'rejects unsafe or unrelated archive path %s', async name => {
      const f = await fixture()
      const archive = Buffer.from(zipSync({ ...f.files, [name]: strToU8('bad') }))
      await expect(extractDesktopArtifact(archive, join(f.root, 'unsafe'))).rejects.toThrow('Unsafe')
    })

  it('rejects case collisions, symlinks and oversized content before extraction', async () => {
    const f = await fixture()
    const duplicate = Buffer.from(zipSync({ ...f.files, 'desktop/dist/INDEX.mjs': strToU8('duplicate') }))
    await expect(extractDesktopArtifact(duplicate, join(f.root, 'duplicate'))).rejects.toThrow('Unsafe')
    const central = f.archive.indexOf(Buffer.from([0x50, 0x4b, 0x01, 0x02]))
    const symlink = Buffer.from(f.archive)
    symlink.writeUInt32LE(0xa000 * 65536, central + 38)
    await expect(extractDesktopArtifact(symlink, join(f.root, 'symlink'))).rejects.toThrow('Unsafe')
    const oversized = Buffer.from(f.archive)
    const fileHeader = oversized.lastIndexOf(Buffer.from('desktop/dist/index.mjs')) - 46
    oversized.writeUInt32LE(513 * 1024 * 1024, fileHeader + 24)
    await expect(extractDesktopArtifact(oversized, join(f.root, 'oversized'))).rejects.toThrow('content limit')
    expect(await fs.readdir(f.root)).toEqual(['original'])
  })

  it('cancels an incomplete transfer and removes staging files', async () => {
    const f = await fixture()
    const read = vi.fn(async ({ offset }: { offset: number }) => ({ totalBytes: f.archive.length + 1,
      dataBase64: offset === 0 ? f.archive.toString('base64') : '' }))
    const cache = new DesktopPluginArtifactCache(join(f.root, 'cache'))
    await expect(cache.prepare(f.source, f.request, read, () => true)).rejects.toThrow('chunk')
    expect(read).toHaveBeenLastCalledWith({ id: f.request.pluginId, revision: f.request.revision, offset: -1 })
    expect(await fs.readdir(join(f.root, 'cache', f.source, f.request.pluginId))).toEqual([])
  })

  it('isolates identical plugins by workspace and keeps removed content until its route is released', async () => {
    const f = await fixture()
    let installed = true
    const sources: {
      manager: DesktopPluginModules
      request: typeof f.request & { sourceKey: string }
      entryUrl: string
    }[] = []
    const cache = new DesktopPluginArtifactCache(join(f.root, 'cache'))
    for (const workspacePath of ['/one', '/two']) {
      const sourceKey = desktopPluginSourceKey('host/stack', workspacePath)
      const identity = {}
      const manager = new DesktopPluginModules({
        connection: () => ({ identity, source: 'host/stack', remote: true, supported: true, workspacePath,
          read: f.read, list: async () => ({ workspacePath, plugins: installed
            ? [{ id: f.request.pluginId, installed: true, enabled: true, desktop: { revision: f.request.revision } }] : [] })
        }), cache, grants: () => [sourceKey], saveGrants: async () => {}
      })
      await manager.context()
      const request = { ...f.request, sourceKey }
      const route = await manager.register(request)
      sources.push({ manager, request, entryUrl: route.entryUrl })
    }
    expect(sources[0].entryUrl).not.toBe(sources[1].entryUrl)
    expect(f.read).toHaveBeenCalledTimes(2)
    installed = false
    for (const { manager, entryUrl } of sources) {
      await manager.context()
      expect((await handlePluginFileRequest(new Request(entryUrl))).status).toBe(200)
    }
    await sources[0].manager.remove(sources[0].request)
    expect((await handlePluginFileRequest(new Request(sources[0].entryUrl))).status).toBe(403)
    expect((await handlePluginFileRequest(new Request(sources[1].entryUrl))).status).toBe(200)
    await sources[1].manager.remove(sources[1].request)
    for (const { manager, request } of sources) {
      expect(await fs.readdir(join(f.root, 'cache', request.sourceKey, request.pluginId))).toEqual([])
      manager.dispose()
    }
  })

  it('does not load local code when a remote server lacks Desktop artifact support', async () => {
    const f = await fixture()
    const connection = { identity: {}, source: 'host/stack', workspacePath: '/remote', remote: true, supported: false,
      list: vi.fn(), read: f.read }
    const manager = new DesktopPluginModules({ connection: () => connection,
      cache: new DesktopPluginArtifactCache(join(f.root, 'cache')),
      grants: () => [], saveGrants: vi.fn()
    })
    const source = await manager.context()
    expect(source.trusted).toBe(false)
    await expect(manager.register({ ...f.request, rootPath: join(f.root, 'original'), sourceKey: source.sourceKey }))
      .rejects.toThrow('does not support Desktop plugin artifacts')
    expect(f.read).not.toHaveBeenCalled()
    expect(connection.list).not.toHaveBeenCalled()
    manager.dispose()
  })

  it('requires workspace authorization and withdraws routes when it is revoked', async () => {
    const f = await fixture()
    let grants: string[] = []
    const connection: DesktopPluginConnection = {
      identity: {}, remote: true, source: 'host/stack', workspacePath: '/wrong-local', supported: true,
      list: async () => ({ workspacePath: '/workspace', plugins: [{ id: f.request.pluginId, installed: true, enabled: true, desktop: { revision: f.request.revision } }] }),
      read: f.read
    }
    const manager = new DesktopPluginModules({ connection: () => connection,
      cache: new DesktopPluginArtifactCache(join(f.root, 'cache')),
      grants: () => grants, saveGrants: async next => { grants = next }
    })
    const context = await manager.context()
    expect(context.trusted).toBe(false)
    const request = { ...f.request, sourceKey: context.sourceKey }
    await expect(manager.register(request)).rejects.toThrow('authorized')
    expect(f.read).not.toHaveBeenCalled()
    await manager.setTrusted(context.sourceKey, true)
    const route = await manager.register(request)
    expect(route.entryUrl).toContain(`/source/${context.sourceKey}/`)
    expect(await (await handlePluginFileRequest(new Request(route.entryUrl))).text()).toContain('remote = true')
    await manager.setTrusted(context.sourceKey, false)
    expect((await handlePluginFileRequest(new Request(route.entryUrl))).status).toBe(403)
    expect(grants).toEqual([])
    manager.dispose()
  })

  it('never activates a completed download after switching connections', async () => {
    const f = await fixture()
    let identity = {}
    let lists = 0
    const manager = new DesktopPluginModules({
      connection: () => ({ identity, source: 'host/stack', remote: true, supported: true, workspacePath: '/workspace',
        list: async () => {
          if (++lists > 1) identity = {}
          return { workspacePath: '/workspace', plugins: [
            { id: f.request.pluginId, installed: true, enabled: true, desktop: { revision: f.request.revision } }
          ] }
        },
        read: f.read
      }), cache: new DesktopPluginArtifactCache(join(f.root, 'cache')),
      grants: () => [f.source], saveGrants: async () => {}
    })
    await manager.context()
    await expect(manager.register({ ...f.request, sourceKey: f.source })).rejects.toThrow('source changed')
    manager.dispose()
  })
})
