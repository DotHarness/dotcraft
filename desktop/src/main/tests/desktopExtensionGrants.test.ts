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
  await fs.mkdir(path.join(root, '.craft-plugin'), { recursive: true })
  await fs.writeFile(path.join(root, '.craft-plugin', 'plugin.json'), JSON.stringify({
    schemaVersion: 1,
    id: 'workflow',
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
  return root
}

describe('desktop extension App Surface grants', () => {
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
