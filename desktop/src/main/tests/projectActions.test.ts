import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { ProjectActionPreferences } from '../../shared/projectActions'
import { discoverProjectEnvironments } from '../projectActions/discovery'
import { ProjectActionsService } from '../projectActions/service'

let root: string
let workspace: string

function writeEnvironment(folder: string, file: string, content: unknown): string {
  const directory = join(folder, '.craft', 'environments')
  mkdirSync(directory, { recursive: true })
  const path = join(directory, file)
  writeFileSync(path, typeof content === 'string' ? content : JSON.stringify(content), 'utf8')
  return path
}

function service(platform: NodeJS.Platform = 'win32') {
  const preferences = new Map<string, ProjectActionPreferences>()
  const instance = new ProjectActionsService({
    readPreferences: (key) => preferences.get(key),
    writePreferences: async (key, value) => {
      preferences.set(key, value)
    },
    notifyChanged: () => {},
    platform
  })
  return { instance, preferences }
}

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'dotcraft-actions-'))
  mkdirSync(join(root, 'repo', '.git'), { recursive: true })
  workspace = join(root, 'repo', 'apps', 'web')
  mkdirSync(workspace, { recursive: true })
})

afterEach(() => {
  rmSync(root, { recursive: true, force: true })
})

describe('project environment discovery', () => {
  it('reads the workspace and its parents up to the git root, nearest environment.json first', async () => {
    writeEnvironment(root, 'environment.json', { name: 'Outside', actions: [] })
    writeEnvironment(join(root, 'repo'), 'environment.json', { name: 'Repo', actions: [] })
    writeEnvironment(join(root, 'repo'), 'ci.json', { name: 'CI', actions: [] })
    writeEnvironment(workspace, 'b.json', { name: 'B', actions: [] })
    writeEnvironment(workspace, 'environment.json', { name: 'Web', actions: [] })
    writeEnvironment(workspace, 'a.json', { actions: [] })

    const environments = await discoverProjectEnvironments(workspace, 'win32')

    expect(environments.map((environment) => [environment.name, environment.isDefault])).toEqual([
      ['Web', true],
      ['a', false],
      ['B', false],
      ['CI', false],
      ['Repo', false]
    ])
    expect(environments[0].id).toBe('.craft/environments/environment.json')
    expect(environments[3].id).toBe('../../.craft/environments/ci.json')
    expect(environments[3].root).toBe(join(root, 'repo'))
  })

  it('trims strings, drops incomplete actions, filters other platforms, and falls back to the tool icon', async () => {
    writeEnvironment(workspace, 'environment.json', {
      version: 1,
      name: '  Web  ',
      actions: [
        { name: ' Run ', icon: 'run', command: ' npm start ' },
        { name: 'Blank', command: '   ' },
        { name: 'Mac only', command: 'open .', platform: 'darwin' },
        { name: 'Windows only', icon: 'rocket', command: 'start .', platform: 'win32' },
        { name: 'Run', icon: 'test', command: 'npm test' }
      ]
    })

    const [environment] = await discoverProjectEnvironments(workspace, 'win32')

    expect(environment.name).toBe('Web')
    expect(environment.actions).toEqual([
      { id: 'Run', name: 'Run', icon: 'run', command: 'npm start' },
      { id: 'Windows only', name: 'Windows only', icon: 'tool', command: 'start .' },
      { id: 'Run#2', name: 'Run', icon: 'test', command: 'npm test' }
    ])
  })

  it('lists unparseable and invalid files as environments that need attention', async () => {
    writeEnvironment(workspace, 'environment.json', '{ "actions": [')
    writeEnvironment(workspace, 'platform.json', { actions: [{ name: 'Run', command: 'x', platform: 'windows' }] })
    writeEnvironment(workspace, 'shape.json', { actions: {} })

    const environments = await discoverProjectEnvironments(workspace, 'linux')

    expect(environments).toHaveLength(3)
    for (const environment of environments) {
      expect(environment.error).toEqual(expect.any(String))
      expect(environment.actions).toEqual([])
    }
  })
})

describe('project actions service', () => {
  it('orders actions by most recent run and remembers the selected environment per project', async () => {
    writeEnvironment(workspace, 'environment.json', {
      actions: [
        { name: 'Build', command: 'npm run build' },
        { name: 'Test', icon: 'test', command: 'npm test' }
      ]
    })
    writeEnvironment(workspace, 'other.json', { name: 'Other', actions: [{ name: 'Lint', command: 'npm run lint' }] })
    const { instance, preferences } = service()

    const initial = await instance.list(workspace)
    expect(initial.selectedEnvironmentId).toBe('.craft/environments/environment.json')
    expect(initial.environments[0].actions.map((action) => action.id)).toEqual(['Build', 'Test'])

    const run = await instance.prepareRun({
      workspacePath: workspace,
      environmentId: '.craft/environments/environment.json',
      actionId: 'Test'
    })
    expect(run).toEqual({ cwd: workspace, command: 'npm test', title: 'Test' })
    expect((await instance.list(workspace)).environments[0].actions.map((action) => action.id)).toEqual(['Test', 'Build'])

    await instance.select(workspace, '.craft/environments/other.json')
    expect((await instance.list(workspace)).selectedEnvironmentId).toBe('.craft/environments/other.json')
    expect([...preferences.keys()]).toEqual([workspace])

    await instance.select(workspace, '.craft/environments/removed.json')
    expect((await instance.list(workspace)).selectedEnvironmentId).toBe('.craft/environments/environment.json')
  })

  it('creates environment.json in the workspace when no environment exists', async () => {
    const { instance } = service()

    const result = await instance.add({
      workspacePath: workspace,
      revision: null,
      action: { name: ' Dev ', icon: 'run', command: ' npm run dev ' }
    })

    expect(result).toEqual({
      ok: true,
      environmentId: '.craft/environments/environment.json',
      action: { id: 'Dev', name: 'Dev', icon: 'run', command: 'npm run dev' }
    })
    expect(JSON.parse(readFileSync(join(workspace, '.craft', 'environments', 'environment.json'), 'utf8'))).toEqual({
      version: 1,
      name: 'web',
      actions: [{ name: 'Dev', icon: 'run', command: 'npm run dev' }]
    })
  })

  it('appends to the environment file and keeps its other content', async () => {
    const path = writeEnvironment(workspace, 'environment.json', {
      version: 1,
      name: 'Web',
      extra: { keep: true },
      actions: [{ name: 'Mac', command: 'open .', platform: 'darwin' }]
    })
    const { instance } = service()
    const [environment] = (await instance.list(workspace)).environments

    const result = await instance.add({
      workspacePath: workspace,
      environmentId: environment.id,
      revision: environment.revision,
      action: { name: 'Test', icon: 'test', command: 'npm test', platform: 'win32' }
    })

    expect(result).toMatchObject({ ok: true, action: { id: 'Test', command: 'npm test' } })
    expect(JSON.parse(readFileSync(path, 'utf8'))).toEqual({
      version: 1,
      name: 'Web',
      extra: { keep: true },
      actions: [
        { name: 'Mac', command: 'open .', platform: 'darwin' },
        { name: 'Test', icon: 'test', command: 'npm test', platform: 'win32' }
      ]
    })
  })

  it('returns no action to run after saving one restricted to another platform', async () => {
    writeEnvironment(workspace, 'environment.json', { actions: [{ name: 'Run', command: 'npm start' }] })
    const { instance } = service('win32')
    const [environment] = (await instance.list(workspace)).environments

    const result = await instance.add({
      workspacePath: workspace,
      environmentId: environment.id,
      revision: environment.revision,
      action: { name: 'Run', icon: 'run', command: 'make run', platform: 'linux' }
    })

    expect(result).toEqual({ ok: true, environmentId: environment.id, action: null })
  })

  it('writes nothing when the environment changed or appeared on disk after the dialog opened', async () => {
    const path = writeEnvironment(workspace, 'environment.json', { actions: [] })
    const { instance } = service()
    const [environment] = (await instance.list(workspace)).environments
    writeFileSync(path, JSON.stringify({ actions: [{ name: 'Edited', command: 'echo' }] }), 'utf8')
    const edited = readFileSync(path, 'utf8')

    const result = await instance.add({
      workspacePath: workspace,
      environmentId: environment.id,
      revision: environment.revision,
      action: { name: 'Test', icon: 'test', command: 'npm test' }
    })

    expect(result).toEqual({ ok: false, reason: 'conflict' })
    expect(readFileSync(path, 'utf8')).toBe(edited)

    await expect(instance.add({
      workspacePath: workspace,
      revision: null,
      action: { name: 'Dev', icon: 'run', command: 'npm run dev' }
    })).resolves.toEqual({ ok: false, reason: 'conflict' })
    expect(readFileSync(path, 'utf8')).toBe(edited)
  })
})
