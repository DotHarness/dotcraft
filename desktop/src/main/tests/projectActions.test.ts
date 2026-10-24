import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { EnvironmentDocument, ProjectActionPreferences } from '../../shared/projectActions'
import { discoverProjectEnvironments } from '../projectActions/discovery'
import { normalizeProjectActionPreferences } from '../projectActions/preferences'
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

  it('accepts setup and cleanup scripts with platform overrides', async () => {
    writeEnvironment(workspace, 'environment.json', {
      setup: { script: 'npm install', win32: { script: 'npm install --no-fund' } },
      cleanup: { script: '' },
      actions: [{ name: 'Run', command: 'npm start' }]
    })

    const [environment] = await discoverProjectEnvironments(workspace, 'win32')

    expect(environment.error).toBeUndefined()
    expect(environment.actions.map((action) => action.id)).toEqual(['Run'])
  })

  it('lists unparseable and invalid files as environments that need attention', async () => {
    writeEnvironment(workspace, 'environment.json', '{ "actions": [')
    writeEnvironment(workspace, 'platform.json', { actions: [{ name: 'Run', command: 'x', platform: 'windows' }] })
    writeEnvironment(workspace, 'shape.json', { actions: {} })

    writeEnvironment(workspace, 'setup.json', { setup: 'npm install' })
    writeEnvironment(workspace, 'override.json', { cleanup: { script: 'down', linux: 'down' } })

    const environments = await discoverProjectEnvironments(workspace, 'linux')

    expect(environments).toHaveLength(5)
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
        { name: 'Mac', icon: 'tool', command: 'open .', platform: 'darwin' },
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

  it('records a newly added action as the most recent one', async () => {
    writeEnvironment(workspace, 'environment.json', {
      actions: [{ name: 'Build', command: 'npm run build' }, { name: 'Lint', command: 'npm run lint' }]
    })
    const { instance } = service()
    const [environment] = (await instance.list(workspace)).environments

    await instance.add({
      workspacePath: workspace,
      environmentId: environment.id,
      revision: environment.revision,
      action: { name: 'Test', icon: 'test', command: 'npm test' }
    })

    expect((await instance.list(workspace)).environments[0].actions.map((action) => action.id))
      .toEqual(['Test', 'Build', 'Lint'])
  })

  it('remembers No environment as an explicit selection', async () => {
    writeEnvironment(workspace, 'environment.json', { actions: [] })
    const { instance, preferences } = service()

    await instance.select(workspace, null)

    expect((await instance.list(workspace)).selectedEnvironmentId).toBeNull()
    expect(preferences.get(workspace)).toEqual({ selectedEnvironmentId: null })
    expect(normalizeProjectActionPreferences({ [workspace]: { selectedEnvironmentId: null } }))
      .toEqual({ [workspace]: { selectedEnvironmentId: null } })
    expect(normalizeProjectActionPreferences({ [workspace]: { selectedEnvironmentId: 3 } })).toBeUndefined()
  })

  it('remembers the worktree environment per project, starting with No environment', async () => {
    writeEnvironment(workspace, 'environment.json', { actions: [] })
    writeEnvironment(workspace, 'ci.json', { name: 'CI', actions: [] })
    const { instance, preferences } = service()

    expect(await instance.listWorktreeEnvironments(workspace)).toMatchObject({ selectedEnvironmentId: null, unavailable: false })

    await instance.selectWorktreeEnvironment(workspace, '.craft/environments/environment.json')

    expect(await instance.listWorktreeEnvironments(workspace)).toMatchObject({
      selectedEnvironmentId: '.craft/environments/environment.json',
      unavailable: false
    })
    expect((await instance.list(workspace)).selectedEnvironmentId).toBe('.craft/environments/environment.json')
    expect(normalizeProjectActionPreferences(Object.fromEntries(preferences)))
      .toEqual({ [workspace]: { worktreeEnvironmentId: '.craft/environments/environment.json' } })

    await instance.selectWorktreeEnvironment(workspace, null)
    expect(await instance.listWorktreeEnvironments(workspace)).toMatchObject({ selectedEnvironmentId: null })
    expect(normalizeProjectActionPreferences(Object.fromEntries(preferences)))
      .toEqual({ [workspace]: { worktreeEnvironmentId: null } })
  })

  it('treats a worktree environment whose file is gone as unavailable and as No environment', async () => {
    const path = writeEnvironment(workspace, 'ci.json', { actions: [] })
    const { instance } = service()
    await instance.selectWorktreeEnvironment(workspace, '.craft/environments/ci.json')
    rmSync(path)

    expect(await instance.listWorktreeEnvironments(workspace)).toMatchObject({ selectedEnvironmentId: null, unavailable: true })
  })

  it('runs an action at the same relative location inside a worktree', async () => {
    writeEnvironment(workspace, 'environment.json', { actions: [{ name: 'Run', command: 'npm start' }] })
    writeEnvironment(join(root, 'repo'), 'repo.json', { actions: [{ name: 'Root', command: 'make' }] })
    const worktree = join(root, 'worktrees', 'feature')
    const { instance } = service()

    await expect(instance.prepareRun({
      workspacePath: workspace,
      environmentId: '.craft/environments/environment.json',
      actionId: 'Run',
      worktreePath: worktree
    })).resolves.toMatchObject({ cwd: join(worktree, 'apps', 'web') })
    await expect(instance.prepareRun({
      workspacePath: workspace,
      environmentId: '../../.craft/environments/repo.json',
      actionId: 'Root',
      worktreePath: worktree
    })).resolves.toMatchObject({ cwd: worktree })
  })

  it('prepares the platform setup script in the execution workspace with both variables naming it', async () => {
    writeEnvironment(workspace, 'environment.json', {
      setup: { script: 'npm install', win32: { script: 'npm install --no-fund' } }
    })
    writeEnvironment(workspace, 'empty.json', { setup: { script: '' } })
    const worktree = join(root, 'worktrees', 'feature')
    const { instance } = service('win32')

    await expect(instance.prepareSetup({
      workspacePath: workspace,
      environmentId: '.craft/environments/environment.json',
      worktreePath: worktree
    })).resolves.toEqual({
      cwd: join(worktree, 'apps', 'web'),
      command: 'npm install --no-fund',
      env: { DOTCRAFT_SOURCE_TREE_PATH: worktree, DOTCRAFT_WORKTREE_PATH: worktree }
    })
    await expect(service('linux').instance.prepareSetup({
      workspacePath: workspace,
      environmentId: '.craft/environments/environment.json'
    })).resolves.toEqual({
      cwd: workspace,
      command: 'npm install',
      env: { DOTCRAFT_SOURCE_TREE_PATH: workspace, DOTCRAFT_WORKTREE_PATH: workspace }
    })
    await expect(instance.prepareSetup({
      workspacePath: workspace,
      environmentId: '.craft/environments/empty.json'
    })).resolves.toBeNull()
  })
})

describe('environment editor files', () => {
  const document: EnvironmentDocument = {
    name: ' Web ',
    setup: { default: ' npm install ', win32: ' npm install --no-fund ', linux: '  ' },
    cleanup: { default: '' },
    actions: [
      { name: 'Run', icon: 'run', command: 'npm start' },
      { name: 'Mac', icon: 'debug', command: 'open .', platform: 'darwin' },
      { name: ' ', icon: 'tool', command: '' }
    ]
  }

  function environmentsFolder(folder = workspace): string {
    return join(folder, '.craft', 'environments')
  }

  it('lists own and inherited environments with the next free file name', async () => {
    writeEnvironment(workspace, 'environment.json', { name: 'Web', actions: [] })
    writeEnvironment(workspace, 'broken.json', '{')
    writeEnvironment(join(root, 'repo'), 'ci.json', { actions: [] })
    const { instance } = service()

    const list = await instance.listProjectEnvironments(workspace)

    expect(list.projectPath).toBe(workspace)
    expect(list.environments.map((entry) => [entry.fileName, entry.name, entry.isDefault, entry.inherited]))
      .toEqual([
        ['environment.json', 'Web', true, false],
        ['broken.json', 'broken', false, false],
        ['ci.json', 'ci', false, true]
      ])
    expect(list.environments[1].error).toEqual(expect.any(String))
    expect(list.environments[2].folder).toBe(join(root, 'repo'))
    expect(list.newEnvironmentPath).toBe(join(environmentsFolder(), 'environment-2.json'))

    writeEnvironment(workspace, 'environment-2.json', { actions: [] })
    expect((await instance.listProjectEnvironments(workspace)).newEnvironmentPath)
      .toBe(join(environmentsFolder(), 'environment-3.json'))
    expect((await instance.listProjectEnvironments(join(root, 'repo'))).newEnvironmentPath)
      .toBe(join(environmentsFolder(join(root, 'repo')), 'environment.json'))
  })

  it('creates a new file and reads it back as the same document with every platform action', async () => {
    const { instance } = service('linux')
    const path = join(environmentsFolder(), 'environment.json')

    expect(await instance.readEnvironment({ projectPath: workspace, path }))
      .toEqual({ ok: true, path, revision: null, document: null })

    const written = await instance.writeEnvironment({ projectPath: workspace, path, revision: null, document })
    expect(written).toEqual({ ok: true, path, revision: expect.any(String) })

    const read = await instance.readEnvironment({ projectPath: workspace, path })
    expect(read).toEqual({
      ok: true,
      path,
      revision: written.ok ? written.revision : null,
      document: {
        name: 'Web',
        setup: { default: 'npm install', win32: 'npm install --no-fund' },
        cleanup: { default: '' },
        actions: [
          { name: 'Run', icon: 'run', command: 'npm start' },
          { name: 'Mac', icon: 'debug', command: 'open .', platform: 'darwin' }
        ]
      }
    })
    expect((await instance.list(workspace)).environments[0].actions.map((action) => action.id)).toEqual(['Run'])
  })

  it('keeps unknown top-level fields and removes emptied scripts', async () => {
    const path = writeEnvironment(workspace, 'environment.json', {
      version: 1,
      name: 'Web',
      owner: { team: 'web' },
      setup: { script: 'npm install' },
      cleanup: { script: 'docker compose down' },
      actions: [{ name: 'Run', icon: 'rocket', command: 'npm start' }]
    })
    const { instance } = service()
    const read = await instance.readEnvironment({ projectPath: workspace, path })
    if (!read.ok || !read.document) throw new Error('expected a readable environment')
    expect(read.document.actions).toEqual([{ name: 'Run', icon: 'tool', command: 'npm start' }])

    const result = await instance.writeEnvironment({
      projectPath: workspace,
      path,
      revision: read.revision,
      document: { ...read.document, setup: { default: ' ', darwin: '' } }
    })

    expect(result.ok).toBe(true)
    expect(JSON.parse(readFileSync(path, 'utf8'))).toEqual({
      version: 1,
      name: 'Web',
      owner: { team: 'web' },
      cleanup: { script: 'docker compose down' },
      actions: [{ name: 'Run', icon: 'tool', command: 'npm start' }]
    })
  })

  it('replaces an invalid file and reports its problem when read', async () => {
    const path = writeEnvironment(workspace, 'environment.json', { setup: { script: 1 } })
    const { instance } = service()

    const read = await instance.readEnvironment({ projectPath: workspace, path })
    expect(read).toMatchObject({ ok: true, document: null, error: expect.any(String), revision: expect.any(String) })

    const result = await instance.writeEnvironment({
      projectPath: workspace,
      path,
      revision: read.ok ? read.revision : null,
      document
    })
    expect(result.ok).toBe(true)
    expect((await discoverProjectEnvironments(workspace, 'win32'))[0].error).toBeUndefined()
  })

  it('reports conflicts without writing and accepts an identical write', async () => {
    const path = join(environmentsFolder(), 'environment.json')
    const { instance } = service()
    const first = await instance.writeEnvironment({ projectPath: workspace, path, revision: null, document })
    if (!first.ok) throw new Error('expected the first write to succeed')
    const content = readFileSync(path, 'utf8')

    expect(await instance.writeEnvironment({ projectPath: workspace, path, revision: null, document }))
      .toEqual({ ok: false, reason: 'conflict' })
    expect(await instance.writeEnvironment({ projectPath: workspace, path, revision: 'stale', document }))
      .toEqual({ ok: false, reason: 'conflict' })
    expect(await instance.writeEnvironment({
      projectPath: workspace,
      path: join(environmentsFolder(), 'missing.json'),
      revision: first.revision,
      document
    })).toEqual({ ok: false, reason: 'conflict' })
    expect(readFileSync(path, 'utf8')).toBe(content)
    expect(existsSync(join(environmentsFolder(), 'missing.json'))).toBe(false)

    expect(await instance.writeEnvironment({ projectPath: workspace, path, revision: first.revision, document }))
      .toEqual({ ok: true, path, revision: first.revision })
  })

  it('rejects paths outside the project environment folders', async () => {
    const { instance } = service()
    writeEnvironment(root, 'outside.json', { actions: [] })
    const outside = [
      join(workspace, 'environment.json'),
      join(environmentsFolder(), 'nested', 'environment.json'),
      join(environmentsFolder(), 'environment.txt'),
      join(environmentsFolder(root), 'outside.json'),
      '.craft/environments/environment.json'
    ]

    for (const path of outside) {
      expect(await instance.readEnvironment({ projectPath: workspace, path })).toEqual({ ok: false, reason: 'failed' })
      expect(await instance.writeEnvironment({ projectPath: workspace, path, revision: null, document }))
        .toEqual({ ok: false, reason: 'failed' })
    }
    expect(existsSync(join(workspace, 'environment.json'))).toBe(false)
    expect(await instance.readEnvironment({
      projectPath: workspace,
      path: join(environmentsFolder(join(root, 'repo')), 'environment.json')
    })).toMatchObject({ ok: true, document: null })
  })
})
