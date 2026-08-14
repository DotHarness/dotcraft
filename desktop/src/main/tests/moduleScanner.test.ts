import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdtemp, mkdir, rm, writeFile } from 'fs/promises'
import { join } from 'path'
import { tmpdir } from 'os'

vi.mock('electron', () => ({
  app: { getPath: vi.fn(() => 'X:\\fixtures\\profile') }
}))

import { scanModules } from '../moduleScanner'

function baseManifest(moduleId: string) {
  return {
    moduleId,
    channelName: 'example',
    displayName: 'Example',
    packageName: '@example/channel',
    configFileName: 'example.json',
    supportedTransports: ['websocket'],
    requiresInteractiveSetup: false,
    variant: 'standard'
  }
}

describe('scanModules config descriptors', () => {
  let tempRoot = ''

  afterEach(async () => {
    if (tempRoot) await rm(tempRoot, { recursive: true, force: true })
    tempRoot = ''
  })

  async function writeManifest(moduleId: string, manifest: Record<string, unknown>): Promise<void> {
    const moduleDir = join(tempRoot, moduleId)
    await mkdir(moduleDir, { recursive: true })
    await writeFile(join(moduleDir, 'manifest.json'), JSON.stringify(manifest), 'utf-8')
  }

  it('preserves declared groups and structured enum options', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'channel-modules-'))
    await writeManifest('fixture-grouped', {
      ...baseManifest('fixture-grouped'),
      configGroups: [{ id: 'configuration', displayLabel: 'Configuration' }],
      configDescriptors: [{
        key: 'example.platform',
        displayLabel: 'Platform',
        description: '',
        required: false,
        dataKind: 'enum',
        masked: false,
        interactiveSetupOnly: false,
        group: 'configuration',
        defaultValue: 'primary',
        options: [
          { value: 'primary', displayLabel: 'Primary' },
          { value: 'secondary', displayLabel: 'Secondary' }
        ]
      }]
    })

    const module = (await scanModules({ modulesDirectory: tempRoot }, true))
      .find((entry) => entry.moduleId === 'fixture-grouped')

    expect(module?.configGroups).toEqual([
      expect.objectContaining({ id: 'configuration', displayLabel: 'Configuration' })
    ])
    expect(module?.configDescriptors[0]).toEqual(expect.objectContaining({
      group: 'configuration',
      defaultValue: 'primary',
      options: [
        expect.objectContaining({ value: 'primary', displayLabel: 'Primary' }),
        expect.objectContaining({ value: 'secondary', displayLabel: 'Secondary' })
      ]
    }))
  })

  it('rejects duplicate and unknown group references', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'channel-modules-'))
    await writeManifest('fixture-duplicate-groups', {
      ...baseManifest('fixture-duplicate-groups'),
      configGroups: [
        { id: 'configuration', displayLabel: 'Configuration' },
        { id: 'configuration', displayLabel: 'Duplicate' }
      ],
      configDescriptors: []
    })
    await writeManifest('fixture-unknown-group', {
      ...baseManifest('fixture-unknown-group'),
      configGroups: [{ id: 'configuration', displayLabel: 'Configuration' }],
      configDescriptors: [{
        key: 'example.value',
        displayLabel: 'Value',
        description: '',
        required: false,
        dataKind: 'string',
        masked: false,
        interactiveSetupOnly: false,
        group: 'missing'
      }]
    })

    const moduleIds = (await scanModules({ modulesDirectory: tempRoot }, true))
      .map((entry) => entry.moduleId)

    expect(moduleIds).not.toContain('fixture-duplicate-groups')
    expect(moduleIds).not.toContain('fixture-unknown-group')
  })
})
