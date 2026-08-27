import { describe, expect, it } from 'vitest'
import { buildOratorioProjectDisplayOptions, oratorioProjectDisplay, selectedOratorioProjectValue } from './oratorio-project-display'
import type { OratorioProjectConfig } from './oratorio-settings-model'

function project(overrides: Partial<OratorioProjectConfig> & Pick<OratorioProjectConfig, 'id' | 'provider' | 'projectKey'>): OratorioProjectConfig {
  return { workspacePath: '', profileId: '', enabled: true, ...overrides }
}

describe('Oratorio project display', () => {
  it('keeps canonical values while showing compact GitHub and nested GitLab paths', () => {
    const options = buildOratorioProjectDisplayOptions([
      project({ id: 'github-1', provider: 'github', projectKey: 'sample-org/widget-service', routeProjectKey: 'github:github.com/sample-org/widget-service' }),
      project({ id: 'gitlab-1', provider: 'gitlab', projectKey: 'platform/runtime/oratorio', routeProjectKey: 'gitlab:gitlab.example.com/platform/runtime/oratorio' }),
    ])

    expect(options).toEqual(expect.arrayContaining([
      expect.objectContaining({ value: 'github:github.com/sample-org/widget-service', label: 'sample-org/widget-service', tooltip: 'GitHub · github.com/sample-org/widget-service' }),
      expect.objectContaining({ value: 'gitlab:gitlab.example.com/platform/runtime/oratorio', label: 'runtime/oratorio', tooltip: 'GitLab · gitlab.example.com/platform/runtime/oratorio' }),
    ]))
  })

  it('expands ambiguous paths and adds provider or instance only when still needed', () => {
    const options = buildOratorioProjectDisplayOptions([
      project({ id: 'nested-a', provider: 'gitlab', projectKey: 'group-a/runtime/oratorio', routeProjectKey: 'gitlab:gitlab.example.com/group-a/runtime/oratorio' }),
      project({ id: 'nested-b', provider: 'gitlab', projectKey: 'group-b/runtime/oratorio', routeProjectKey: 'gitlab:gitlab.example.com/group-b/runtime/oratorio' }),
      project({ id: 'github-shared', provider: 'github', projectKey: 'team/shared', routeProjectKey: 'github:github.com/team/shared' }),
      project({ id: 'gitlab-shared', provider: 'gitlab', projectKey: 'team/shared', routeProjectKey: 'gitlab:gitlab.example.com/team/shared' }),
    ])

    expect(options.find((option) => option.projectId === 'nested-a')?.label).toBe('group-a/runtime/oratorio')
    expect(options.find((option) => option.projectId === 'nested-b')?.label).toBe('group-b/runtime/oratorio')
    expect(options.find((option) => option.projectId === 'github-shared')?.label).toBe('GitHub · team/shared')
    expect(options.find((option) => option.projectId === 'gitlab-shared')?.label).toBe('GitLab · team/shared')
  })

  it('matches aliases without changing spelling and safely compacts an unknown canonical value', () => {
    const options = buildOratorioProjectDisplayOptions([
      project({ id: 'github-1', provider: 'github', projectKey: 'sample-org/widget-service', routeProjectKey: 'github:github.com/sample-org/widget-service' }),
    ])

    expect(selectedOratorioProjectValue(['GITHUB:GITHUB.COM/SAMPLE-ORG/WIDGET-SERVICE'], options[0])).toBe('GITHUB:GITHUB.COM/SAMPLE-ORG/WIDGET-SERVICE')
    expect(oratorioProjectDisplay('gitlab:gitlab.example.com/platform/runtime/unknown', options)).toEqual({
      label: 'runtime/unknown',
      tooltip: 'GitLab · gitlab.example.com/platform/runtime/unknown',
    })
  })
})
