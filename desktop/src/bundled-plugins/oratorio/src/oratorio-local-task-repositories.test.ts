import { describe, expect, it } from 'vitest'
import { localTaskRepositoryOptions } from './oratorio-local-task-repositories'
import type { OratorioProjectConfig } from './settings/oratorio-settings-model'

function project(overrides: Partial<OratorioProjectConfig>): OratorioProjectConfig {
  return { id: 'github:acme/app', provider: 'github', projectKey: 'acme/app', workspacePath: '/w/app', profileId: '', enabled: true, ...overrides }
}

describe('local task repositories', () => {
  it('offers only enabled projects with a workspace, keyed the way synced items name them', () => {
    expect(localTaskRepositoryOptions([
      project({}),
      project({ id: 'github:acme/disabled', projectKey: 'acme/disabled', enabled: false }),
      project({ id: 'github:acme/unmapped', projectKey: 'acme/unmapped', workspacePath: '' }),
      project({ id: 'gitlab:group/api', provider: 'gitlab', projectKey: 'group/api', routeProjectKey: 'gitlab:gitlab.example.com/group/api', workspacePath: '/w/api' }),
    ])).toEqual([
      { value: 'acme/app', label: 'acme/app' },
      { value: 'gitlab:gitlab.example.com/group/api', label: 'group/api' },
    ])
  })
})
