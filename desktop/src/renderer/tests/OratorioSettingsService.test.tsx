import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  loadOratorioSettings,
  saveOratorioSettings,
  saveOratorioSyncSchedule
} from '../components/oratorio/settings/oratorio-settings-service'
import { installDesktopApiMock } from './desktopApiMock'

describe('Oratorio settings service', () => {
  let requestMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    requestMock = vi.fn()
    installDesktopApiMock({ oratorio: { request: requestMock } })
  })

  it('round-trips Server-owned runtime fields while saving visible settings', async () => {
    const configuration = {
      runtime: {
        managedWorktreesEnabled: true,
        worktreeRoot: '',
        worktreeBranchPrefix: 'oratorio/run',
        globalMaxActiveRuns: 7,
        maxActiveRunsPerRepository: 4,
        maxActiveRunsPerSource: 5,
        maxRunAttempts: 9,
        retryBackoffSeconds: 25,
        maxRetryBackoffSeconds: 420,
        stallTimeoutSeconds: 900,
        succeededWorktreeRetentionHours: 72,
        failedWorktreeRetentionHours: 240,
        worktreeCleanupEnabled: false,
        worktreeCleanupIntervalSeconds: 180,
        serverOnlyFlag: 'preserve-me'
      }
    }
    let savedConfiguration: Record<string, any> | undefined

    requestMock.mockImplementation(async (request: { method: string; path: string; body?: Record<string, any> }) => {
      if (request.method === 'GET' && request.path === '/api/v1/settings/server-configuration') {
        return { status: 200, data: { revision: '17', restartRequired: false, configuration } }
      }
      if (request.method === 'GET' && request.path === '/api/v1/sources/sync-schedules') {
        return { status: 200, data: { schedules: [] } }
      }
      if (request.method === 'PUT' && request.path === '/api/v1/settings/server-configuration') {
        savedConfiguration = request.body?.configuration
        return {
          status: 200,
          data: {
            configuration: {
              revision: '18',
              restartRequired: true,
              configuration: savedConfiguration
            }
          }
        }
      }
      return { status: 200, data: {} }
    })

    const loaded = await loadOratorioSettings()
    loaded.settings.worktreeRoot = 'C:\\example\\oratorio-worktrees'
    const saved = await saveOratorioSettings(loaded.settings, loaded.serverConfiguration)

    expect(savedConfiguration?.runtime).toMatchObject({
      worktreeRoot: 'C:\\example\\oratorio-worktrees',
      globalMaxActiveRuns: 7,
      maxActiveRunsPerRepository: 4,
      maxActiveRunsPerSource: 5,
      maxRunAttempts: 9,
      retryBackoffSeconds: 25,
      maxRetryBackoffSeconds: 420,
      stallTimeoutSeconds: 900,
      succeededWorktreeRetentionHours: 72,
      failedWorktreeRetentionHours: 240,
      worktreeCleanupEnabled: false,
      worktreeCleanupIntervalSeconds: 180,
      serverOnlyFlag: 'preserve-me'
    })
    expect(saved.restartRequired).toBe(true)
    expect(requestMock.mock.calls.some(([request]) =>
      request.method === 'PUT' && request.path.endsWith('/sync-schedule')
    )).toBe(false)
  })

  it('normalizes labels from the Server and in the saved configuration', async () => {
    let savedConfiguration: Record<string, any> | undefined
    requestMock.mockImplementation(async (request: { method: string; path: string; body?: Record<string, any> }) => {
      if (request.method === 'GET' && request.path === '/api/v1/settings/server-configuration') {
        return {
          status: 200,
          data: {
            revision: '1',
            restartRequired: false,
            configuration: {
              automation: {
                autoDispatchAllowLabels: [' oratorio:auto ', 'ORATORIO:AUTO', ''],
                autoDispatchBlockLabels: ['blocked', ' BLOCKED ', 'on hold']
              }
            }
          }
        }
      }
      if (request.method === 'GET' && request.path === '/api/v1/sources/sync-schedules') {
        return { status: 200, data: { schedules: [] } }
      }
      if (request.method === 'PUT' && request.path === '/api/v1/settings/server-configuration') {
        savedConfiguration = request.body?.configuration
        return {
          status: 200,
          data: {
            configuration: {
              revision: '2',
              restartRequired: false,
              configuration: savedConfiguration
            }
          }
        }
      }
      throw new Error(`Unexpected request: ${request.method} ${request.path}`)
    })

    const loaded = await loadOratorioSettings()
    expect(loaded.settings.allowedLabels).toEqual(['oratorio:auto'])
    expect(loaded.settings.blockedLabels).toEqual(['blocked', 'on hold'])

    loaded.settings.allowedLabels = ['oratorio:auto', ' ORATORIO:AUTO ', 'frontend', '']
    loaded.settings.blockedLabels = ['blocked', ' BLOCKED ', 'on hold']
    await saveOratorioSettings(loaded.settings, loaded.serverConfiguration)

    expect(savedConfiguration?.automation.autoDispatchAllowLabels).toEqual(['oratorio:auto', 'frontend'])
    expect(savedConfiguration?.automation.autoDispatchBlockLabels).toEqual(['blocked', 'on hold'])
  })

  it('saves only the requested provider schedule', async () => {
    requestMock.mockResolvedValue({ status: 200, data: {} })

    await saveOratorioSyncSchedule('gitlab', 1800)

    expect(requestMock).toHaveBeenCalledTimes(1)
    expect(requestMock).toHaveBeenCalledWith({
      method: 'PUT',
      path: '/api/v1/sources/gitlab/sync-schedule',
      body: { enabled: true, intervalSeconds: 1800 }
    })
  })

  it('surfaces sync schedule failures instead of silently replacing them with defaults', async () => {
    requestMock.mockImplementation(async (request: { path: string }) => {
      if (request.path === '/api/v1/settings/server-configuration') {
        return { status: 200, data: { revision: '1', restartRequired: false, configuration: {} } }
      }
      throw new Error('sync schedules unavailable')
    })

    await expect(loadOratorioSettings()).rejects.toThrow('sync schedules unavailable')
  })

  it('round-trips canonical repository workspace routes', async () => {
    const configuration = {
      gitHub: {
        endpoint: 'https://api.github.com',
        repositories: ['AkiKurisu/Ceres'],
        installationProfiles: [{
          instance: 'github.com',
          owner: 'AkiKurisu',
          installationId: '123',
          source: 'detected'
        }]
      },
      dotCraft: {
        repositoryWorkspaceRoutes: [{
          project: 'github:github.com/AkiKurisu/Ceres',
          workspacePath: 'C:\\example\\Ceres'
        }]
      }
    }
    let savedConfiguration: Record<string, any> | undefined
    requestMock.mockImplementation(async (request: { method: string; path: string; body?: Record<string, any> }) => {
      if (request.method === 'GET' && request.path === '/api/v1/settings/server-configuration') {
        return { status: 200, data: { revision: '1', restartRequired: false, configuration } }
      }
      if (request.method === 'GET' && request.path === '/api/v1/sources/sync-schedules') {
        return { status: 200, data: { schedules: [] } }
      }
      if (request.method === 'PUT' && request.path === '/api/v1/settings/server-configuration') {
        savedConfiguration = request.body?.configuration
        return {
          status: 200,
          data: { configuration: { revision: '2', restartRequired: false, configuration: savedConfiguration } }
        }
      }
      return { status: 200, data: {} }
    })

    const loaded = await loadOratorioSettings()
    expect(loaded.settings.projects).toHaveLength(1)
    expect(loaded.settings.projects[0]).toMatchObject({
      projectKey: 'AkiKurisu/Ceres',
      routeProjectKey: 'github:github.com/AkiKurisu/Ceres',
      workspacePath: 'C:\\example\\Ceres',
      enabled: true
    })

    await saveOratorioSettings(loaded.settings, loaded.serverConfiguration)

    expect(savedConfiguration?.dotCraft.repositoryWorkspaceRoutes).toEqual([{
      project: 'github:github.com/AkiKurisu/Ceres',
      workspacePath: 'C:\\example\\Ceres'
    }])
    expect(savedConfiguration?.dotCraft.repositoryWorkspaces).toBeUndefined()
  })
})
