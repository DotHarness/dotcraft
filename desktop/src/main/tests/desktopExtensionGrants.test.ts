import { afterEach, describe, expect, it } from 'vitest'
import { promises as fs } from 'fs'
import * as os from 'os'
import * as path from 'path'
import {
  authorizeDesktopExtensionGrant,
  clearDesktopExtensionGrants,
  ensureDesktopExtensionAppAllowed,
  ensureDesktopExtensionAppSurfaceAllowed,
  requireDesktopExtensionGrant
} from '../desktopExtensionGrants'

const temporaryRoots: string[] = []

afterEach(async () => {
  clearDesktopExtensionGrants()
  await Promise.all(temporaryRoots.splice(0).map((root) => fs.rm(root, { recursive: true, force: true })))
})

async function createPlugin(requiredAppSurfaces: unknown): Promise<string> {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'dotcraft-desktop-extension-grant-'))
  temporaryRoots.push(root)
  await writePlugin(root, requiredAppSurfaces)
  return root
}

async function writePlugin(
  root: string,
  requiredAppSurfaces: unknown,
  pluginId = 'workflow'
): Promise<void> {
  await fs.mkdir(path.join(root, '.craft-plugin'), { recursive: true })
  await fs.writeFile(path.join(root, '.craft-plugin', 'plugin.json'), JSON.stringify({
    schemaVersion: 1,
    id: pluginId,
    desktopExtensions: './desktop-extensions.json'
  }))
  await fs.writeFile(path.join(root, 'desktop-extensions.json'), JSON.stringify({
    extensions: [{
      id: 'workflow-board',
      entry: './desktop/workflow-board.mjs',
      requiredAppIds: ['com.example.legacy'],
      connectOrigins: ['http://127.0.0.1:*'],
      surfaceWriteScopes: ['legacy.write'],
      requiredAppSurfaces
    }]
  }))
}

describe('desktop extension App Surface grants', () => {
  it('uses a matching locally bundled plugin when the reported remote root is unavailable', async () => {
    const bundledContainer = await fs.mkdtemp(path.join(os.tmpdir(), 'dotcraft-bundled-plugins-'))
    temporaryRoots.push(bundledContainer)
    const bundledPluginRoot = path.join(bundledContainer, 'sample-plugin')
    await writePlugin(bundledPluginRoot, [], 'sample-plugin')

    const authorization = await authorizeDesktopExtensionGrant({
      pluginId: 'sample-plugin',
      rootPath: '/test-fixtures/remote/sample-plugin',
      extensionId: 'workflow-board'
    }, { bundledRootPaths: [bundledContainer] })

    expect(authorization.rootPath).toBe(await fs.realpath(bundledPluginRoot))
    expect(requireDesktopExtensionGrant(authorization.grantId).rootPath).toBe(authorization.rootPath)
  })

  it('derives app, surface, and access authority from the verified descriptor', async () => {
    const rootPath = await createPlugin([
      { appId: 'com.example.workflow', surfaceId: 'board', access: ['read', 'write'] },
      { appId: 'com.example.workflow', surfaceId: 'summary', access: ['read'] }
    ])

    const { grantId } = await authorizeDesktopExtensionGrant({
      pluginId: 'workflow',
      rootPath,
      extensionId: 'workflow-board'
    })
    const grant = requireDesktopExtensionGrant(grantId)

    expect(grant.requiredAppSurfaces).toEqual([
      { appId: 'com.example.workflow', surfaceId: 'board', access: ['read', 'write'] },
      { appId: 'com.example.workflow', surfaceId: 'summary', access: ['read'] }
    ])
    expect(grant.requiredAppIds).toEqual(['com.example.legacy'])
    expect(() => ensureDesktopExtensionAppAllowed(grant, 'com.example.legacy')).not.toThrow()
    expect(() => ensureDesktopExtensionAppAllowed(grant, 'com.example.workflow')).toThrow('not allowed to access app')
    expect(() => ensureDesktopExtensionAppSurfaceAllowed(grant, 'com.example.workflow', 'board', 'write')).not.toThrow()
    expect(() => ensureDesktopExtensionAppSurfaceAllowed(grant, 'com.example.workflow', 'summary', 'write')).toThrow('not allowed')
    expect(() => ensureDesktopExtensionAppSurfaceAllowed(grant, 'com.example.other', 'board', 'read')).toThrow('not allowed')
  })

  it('rejects duplicate surface grants and invalid access values', async () => {
    const duplicateRoot = await createPlugin([
      { appId: 'com.example.workflow', surfaceId: 'board', access: ['read'] },
      { appId: 'com.example.workflow', surfaceId: 'board', access: ['write'] }
    ])
    await expect(authorizeDesktopExtensionGrant({
      pluginId: 'workflow',
      rootPath: duplicateRoot,
      extensionId: 'workflow-board'
    })).rejects.toThrow('duplicate app surface')

    const invalidAccessRoot = await createPlugin([
      { appId: 'com.example.workflow', surfaceId: 'board', access: ['admin'] }
    ])
    await expect(authorizeDesktopExtensionGrant({
      pluginId: 'workflow',
      rootPath: invalidAccessRoot,
      extensionId: 'workflow-board'
    })).rejects.toThrow('invalid access')
  })
})
