import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

const {
  protocolHandleMock,
  protocolRegisterSchemesAsPrivilegedMock,
  netFetchMock
} = vi.hoisted(() => ({
  protocolHandleMock: vi.fn(),
  protocolRegisterSchemesAsPrivilegedMock: vi.fn(),
  netFetchMock: vi.fn()
}))

vi.mock('electron', () => ({
  protocol: {
    registerSchemesAsPrivileged: protocolRegisterSchemesAsPrivilegedMock,
    handle: protocolHandleMock
  },
  net: {
    fetch: netFetchMock
  }
}))

import {
  PLUGIN_FILE_SCHEME,
  authorizePluginRoot,
  buildPluginFileUrl,
  clearAuthorizedPluginRoots,
  handlePluginFileRequest,
  pluginUrlToPath,
  registerPluginFileScheme
} from '../pluginFileProtocol'

const tempDirs: string[] = []

beforeEach(() => {
  clearAuthorizedPluginRoots()
  protocolHandleMock.mockReset()
  protocolRegisterSchemesAsPrivilegedMock.mockReset()
  netFetchMock.mockReset()
  netFetchMock.mockResolvedValue(new Response('export {}', {
    status: 200,
    headers: {
      'X-Upstream': 'ok'
    }
  }))
})

afterEach(() => {
  clearAuthorizedPluginRoots()
  for (const dir of tempDirs.splice(0)) {
    rmSync(dir, { recursive: true, force: true })
  }
})

function createTempDir(): string {
  const dir = mkdtempSync(join(tmpdir(), 'plugin-file-protocol-'))
  tempDirs.push(dir)
  return dir
}

function createPluginRoot(pluginId = 'agent-teams'): string {
  const root = createTempDir()
  const manifestDir = join(root, '.craft-plugin')
  mkdirSync(manifestDir, { recursive: true })
  writeFileSync(join(manifestDir, 'plugin.json'), JSON.stringify({ id: pluginId }), 'utf8')
  return root
}

function writePluginFile(root: string, relativePath: string, content = 'export {}'): string {
  const absolutePath = join(root, ...relativePath.split('/'))
  mkdirSync(join(absolutePath, '..'), { recursive: true })
  writeFileSync(absolutePath, content, 'utf8')
  return absolutePath
}

describe('registerPluginFileScheme', () => {
  it('registers dotcraft-plugin with CORS-enabled fetch support', () => {
    registerPluginFileScheme()

    expect(protocolRegisterSchemesAsPrivilegedMock).toHaveBeenCalledWith([
      {
        scheme: PLUGIN_FILE_SCHEME,
        privileges: {
          standard: true,
          secure: true,
          supportFetchAPI: true,
          bypassCSP: false,
          stream: true,
          corsEnabled: true
        }
      }
    ])
  })
})

describe('buildPluginFileUrl / pluginUrlToPath', () => {
  it('encodes and decodes Windows paths without turning the drive into a host', () => {
    const url = buildPluginFileUrl(
      'Agent-Teams',
      'Z:\\__dotcraft_fixture__\\plugins\\agent-teams\\desktop\\team-card-board.mjs'
    )

    expect(url).toBe(
      `${PLUGIN_FILE_SCHEME}://agent-teams/Z%3A/__dotcraft_fixture__/plugins/agent-teams/desktop/team-card-board.mjs`
    )
    expect(pluginUrlToPath(url)).toEqual({
      pluginId: 'agent-teams',
      absolutePath: 'Z:/__dotcraft_fixture__/plugins/agent-teams/desktop/team-card-board.mjs'
    })
  })
})

describe('handlePluginFileRequest', () => {
  it('serves authorized plugin modules with JavaScript and CORS headers', async () => {
    const root = createPluginRoot()
    const file = writePluginFile(root, 'desktop/team-card-board.mjs')
    await authorizePluginRoot('agent-teams', root)

    const response = await handlePluginFileRequest(new Request(buildPluginFileUrl('agent-teams', file), {
      headers: {
        Origin: 'http://localhost:5173'
      }
    }))

    expect(response.status).toBe(200)
    expect(response.headers.get('Content-Type')).toBe('text/javascript; charset=utf-8')
    expect(response.headers.get('Access-Control-Allow-Origin')).toBe('http://localhost:5173')
    expect(response.headers.get('Access-Control-Allow-Methods')).toBe('GET, HEAD')
    expect(response.headers.get('Vary')).toContain('Origin')
    expect(await response.text()).toBe('export {}')
    const fetchedUrl = String(netFetchMock.mock.calls[0]?.[0])
    expect(fetchedUrl).toMatch(/^file:\/\//)
    expect(decodeURIComponent(fetchedUrl).replace(/\\/g, '/')).toContain('/desktop/team-card-board.mjs')
  })

  it('rejects requests for plugins that were not authorized', async () => {
    const root = createPluginRoot()
    const file = writePluginFile(root, 'desktop/team-card-board.mjs')

    const response = await handlePluginFileRequest(new Request(buildPluginFileUrl('agent-teams', file), {
      headers: {
        Origin: 'http://localhost:5173'
      }
    }))

    expect(response.status).toBe(403)
    expect(netFetchMock).not.toHaveBeenCalled()
  })

  it('rejects files outside the authorized plugin root', async () => {
    const root = createPluginRoot()
    const outsideRoot = createTempDir()
    const outsideFile = join(outsideRoot, 'outside.mjs')
    writeFileSync(outsideFile, 'export {}', 'utf8')
    await authorizePluginRoot('agent-teams', root)

    const response = await handlePluginFileRequest(new Request(buildPluginFileUrl('agent-teams', outsideFile), {
      headers: {
        Origin: 'http://localhost:5173'
      }
    }))

    expect(response.status).toBe(403)
    expect(netFetchMock).not.toHaveBeenCalled()
  })

  it('rejects directories inside the authorized plugin root', async () => {
    const root = createPluginRoot()
    mkdirSync(join(root, 'desktop'), { recursive: true })
    await authorizePluginRoot('agent-teams', root)

    const response = await handlePluginFileRequest(new Request(buildPluginFileUrl('agent-teams', join(root, 'desktop')), {
      headers: {
        Origin: 'http://localhost:5173'
      }
    }))

    expect(response.status).toBe(403)
    expect(netFetchMock).not.toHaveBeenCalled()
  })

  it('rejects non-loopback web origins', async () => {
    const root = createPluginRoot()
    const file = writePluginFile(root, 'desktop/team-card-board.mjs')
    await authorizePluginRoot('agent-teams', root)

    const response = await handlePluginFileRequest(new Request(buildPluginFileUrl('agent-teams', file), {
      headers: {
        Origin: 'https://example.com'
      }
    }))

    expect(response.status).toBe(403)
    expect(netFetchMock).not.toHaveBeenCalled()
  })
})
