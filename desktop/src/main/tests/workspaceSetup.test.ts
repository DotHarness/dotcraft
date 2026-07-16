import { afterEach, describe, expect, it, vi } from 'vitest'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import {
  applyWorkspaceSetupBootstrapImport,
  createUniqueSetupProviderId,
  detectWorkspaceSetupBootstrapImportSources,
  getWorkspaceStatus,
  listSetupModels,
  shouldRouteWorkspaceThroughSetupBeforeAppServerStart
} from '../workspaceSetup'

const tempDirs: string[] = []

function createTempWorkspace(): string {
  const dir = mkdtempSync(join(tmpdir(), 'dotcraft-workspace-'))
  tempDirs.push(dir)
  return dir
}

function writeJson(path: string, value: unknown): void {
  mkdirSync(join(path, '..'), { recursive: true })
  writeFileSync(path, JSON.stringify(value), 'utf8')
}

afterEach(() => {
  for (const dir of tempDirs.splice(0, tempDirs.length)) {
    rmSync(dir, { recursive: true, force: true })
  }
})

describe('getWorkspaceStatus', () => {
  it('returns no-workspace for empty paths', () => {
    expect(getWorkspaceStatus('', { userConfigPath: join(createTempWorkspace(), '.craft', 'config.json') })).toEqual({
      status: 'no-workspace',
      workspacePath: '',
      hasUserConfig: false,
      providers: []
    })
  })

  it('keeps empty workspace config in setup until a provider is configured', () => {
    const workspace = createTempWorkspace()
    const configPath = join(workspace, '.craft', 'config.json')
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(configPath, '{}', 'utf8')

    expect(getWorkspaceStatus(workspace, { userConfigPath: join(createTempWorkspace(), '.craft', 'config.json') })).toEqual({
      status: 'needs-setup',
      workspacePath: workspace,
      hasUserConfig: false,
      providers: []
    })
  })

  it('returns ready when empty workspace config inherits a valid user default provider', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(join(workspace, '.craft', 'config.json'), '{}', 'utf8')
    writeJson(userConfigPath, {
      ProviderId: 'openai',
      ProviderModels: { openai: 'gpt-4.1' },
      Providers: {
        openai: {
          DisplayName: 'OpenAI',
          Protocol: 'openai-responses',
          ApiKey: 'sk-test'
        }
      }
    })

    expect(getWorkspaceStatus(workspace, { userConfigPath })).toEqual({
      status: 'ready',
      workspacePath: workspace,
      hasUserConfig: true,
      userConfigDefaults: {
        providerId: 'openai',
        model: 'gpt-4.1'
      },
      providers: [
        {
          id: 'openai',
          displayName: 'OpenAI',
          protocol: 'openai-responses',
          hasApiKey: true,
          endPoint: '',
          networkTimeoutSeconds: null
        }
      ]
    })
  })

  it('returns ready when workspace selects a provider from user config', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeJson(join(workspace, '.craft', 'config.json'), {
      ProviderId: 'anthropic'
    })
    writeJson(userConfigPath, {
      ProviderModels: { anthropic: 'claude-sonnet-4-5' },
      Providers: {
        anthropic: {
          DisplayName: 'Anthropic',
          Protocol: 'anthropic',
          ApiKey: 'sk-ant',
          EndPoint: 'https://api.anthropic.com'
        }
      }
    })

    expect(getWorkspaceStatus(workspace, { userConfigPath })).toMatchObject({
      status: 'ready',
      workspacePath: workspace,
      hasUserConfig: true,
      providers: [
        expect.objectContaining({
          id: 'anthropic',
          protocol: 'anthropic'
        })
      ]
    })
  })

  it('returns needs-setup when selected provider is not configured', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    writeJson(join(workspace, '.craft', 'config.json'), {
      ProviderId: 'missing',
      Model: 'gpt-4.1'
    })
    writeJson(userConfigPath, {
      Providers: {
        openai: {
          DisplayName: 'OpenAI',
          Protocol: 'openai-responses',
          ApiKey: 'sk-test'
        }
      }
    })

    expect(getWorkspaceStatus(workspace, { userConfigPath })).toMatchObject({
      status: 'needs-setup',
      workspacePath: workspace,
      hasUserConfig: true
    })
  })

  it('ignores obsolete root model values when no provider model is configured', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    writeJson(join(workspace, '.craft', 'config.json'), {
      ProviderId: 'openai',
      Model: ''
    })
    writeJson(userConfigPath, {
      ProviderId: 'openai',
      Model: 'gpt-4.1',
      Providers: {
        openai: {
          DisplayName: 'OpenAI',
          Protocol: 'openai-responses',
          ApiKey: 'sk-test'
        }
      }
    })

    expect(getWorkspaceStatus(workspace, { userConfigPath })).toMatchObject({
      status: 'needs-setup',
      workspacePath: workspace,
      hasUserConfig: true
    })
  })

  it('returns explicit user providers without exposing legacy OpenAI fields', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(userHome, '.craft'), { recursive: true })
    writeFileSync(
      userConfigPath,
      JSON.stringify({
        ProviderId: 'anthropic',
        ProviderModels: { anthropic: 'claude-sonnet-4-5' },
        ApiKey: 'sk-legacy',
        EndPoint: 'https://legacy.example/v1',
        Providers: {
          openai: {
            DisplayName: 'Legacy OpenAI',
            Protocol: 'openai',
            ApiKey: 'sk-hidden',
            EndPoint: 'https://hidden.example/v1'
          },
          'openai-responses': {
            DisplayName: 'OpenAI Responses',
            Protocol: 'openai-responses',
            ApiKey: 'sk-responses',
            EndPoint: ''
          },
          anthropic: {
            DisplayName: 'Anthropic',
            Protocol: 'anthropic',
            ApiKey: 'sk-ant',
            EndPoint: 'https://api.anthropic.com',
            NetworkTimeoutSeconds: 120
          }
        }
      }),
      'utf8'
    )

    expect(getWorkspaceStatus(workspace, { userConfigPath })).toEqual({
      status: 'needs-setup',
      workspacePath: workspace,
      hasUserConfig: true,
      userConfigDefaults: {
        providerId: 'anthropic',
        model: 'claude-sonnet-4-5'
      },
      providers: [
        {
          id: 'anthropic',
          displayName: 'Anthropic',
          protocol: 'anthropic',
          hasApiKey: true,
          endPoint: 'https://api.anthropic.com',
          networkTimeoutSeconds: 120
        },
        {
          id: 'openai',
          displayName: 'Legacy OpenAI',
          protocol: 'openai-chat-completions',
          hasApiKey: true,
          endPoint: 'https://hidden.example/v1',
          networkTimeoutSeconds: null
        },
        {
          id: 'openai-responses',
          displayName: 'OpenAI Responses',
          protocol: 'openai-responses',
          hasApiKey: true,
          endPoint: '',
          networkTimeoutSeconds: null
        }
      ]
    })
  })
})

describe('shouldRouteWorkspaceThroughSetupBeforeAppServerStart', () => {
  it('routes local setup-required workspaces through setup before AppServer start', () => {
    const workspace = createTempWorkspace()
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(join(workspace, '.craft', 'config.json'), '{}', 'utf8')

    expect(
      shouldRouteWorkspaceThroughSetupBeforeAppServerStart(workspace, {
        userConfigPath: join(createTempWorkspace(), '.craft', 'config.json')
      })
    ).toBe(true)
  })

  it('does not block remote AppServer connection modes', () => {
    const workspace = createTempWorkspace()
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(join(workspace, '.craft', 'config.json'), '{}', 'utf8')

    expect(
      shouldRouteWorkspaceThroughSetupBeforeAppServerStart(workspace, {
        usingRemoteConnection: true,
        userConfigPath: join(createTempWorkspace(), '.craft', 'config.json')
      })
    ).toBe(false)
  })

  it('allows local ready workspaces to start AppServer', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    writeJson(join(workspace, '.craft', 'config.json'), {
      ProviderId: 'openai',
      ProviderModels: { openai: 'gpt-4.1' }
    })
    writeJson(userConfigPath, {
      ProviderModels: { openai: 'gpt-4.1' },
      Providers: {
        openai: {
          DisplayName: 'OpenAI',
          Protocol: 'openai-responses',
          ApiKey: 'sk-test'
        }
      }
    })

    expect(shouldRouteWorkspaceThroughSetupBeforeAppServerStart(workspace, { userConfigPath })).toBe(false)
  })
})

describe('workspace setup bootstrap import detection', () => {
  it('detects nearest AGENTS.md and CLAUDE.md sources in AGENTS.md-first order', () => {
    const workspace = createTempWorkspace()
    const child = join(workspace, 'packages', 'app')
    mkdirSync(child, { recursive: true })
    writeFileSync(join(workspace, 'AGENTS.md'), 'root agents', 'utf8')
    writeFileSync(join(child, 'AGENTS.md'), 'child agents', 'utf8')
    writeFileSync(join(workspace, 'CLAUDE.md'), 'claude', 'utf8')

    expect(detectWorkspaceSetupBootstrapImportSources(child)).toEqual([
      {
        id: 'codex',
        fileName: 'AGENTS.md',
        path: join(child, 'AGENTS.md'),
        relativePath: 'AGENTS.md'
      },
      {
        id: 'claude',
        fileName: 'CLAUDE.md',
        path: join(workspace, 'CLAUDE.md'),
        relativePath: '../../CLAUDE.md'
      }
    ])
  })

  it('includes setup import candidates only while the workspace needs setup', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    writeFileSync(join(workspace, 'AGENTS.md'), 'agents', 'utf8')
    writeJson(userConfigPath, {
      Providers: {
        openai: {
          DisplayName: 'OpenAI',
          Protocol: 'openai-responses',
          ApiKey: 'sk-test'
        }
      }
    })

    expect(getWorkspaceStatus(workspace, { userConfigPath }))
      .toMatchObject({
        status: 'needs-setup',
        bootstrapImportSources: [
          expect.objectContaining({
            id: 'codex',
            fileName: 'AGENTS.md'
          })
        ]
      })

    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(
      join(workspace, '.craft', 'config.json'),
      '{"ProviderId":"openai","ProviderModels":{"openai":"gpt-4.1"}}',
      'utf8'
    )
    expect(getWorkspaceStatus(workspace, { userConfigPath }))
      .not.toHaveProperty('bootstrapImportSources')
  })

  it('copies the selected source over .craft/AGENTS.md and records metadata', () => {
    const workspace = createTempWorkspace()
    writeFileSync(join(workspace, 'CLAUDE.md'), '# Claude rules\n', 'utf8')
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(join(workspace, '.craft', 'AGENTS.md'), '# DotCraft template\n', 'utf8')

    expect(applyWorkspaceSetupBootstrapImport(workspace, 'claude')).toEqual({
      sourceId: 'claude',
      status: 'success'
    })
    expect(readFileSync(join(workspace, '.craft', 'AGENTS.md'), 'utf8')).toBe('# Claude rules\n')
    expect(existsSync(join(workspace, '.craft', 'imports', 'bootstrap-import.json'))).toBe(true)
  })
})

describe('createUniqueSetupProviderId', () => {
  it('uses stable template ids and appends a suffix on conflict', () => {
    expect(createUniqueSetupProviderId('anthropic', [])).toBe('anthropic')
    expect(createUniqueSetupProviderId('anthropic', [{ id: 'anthropic' }])).toBe('anthropic-2')
    expect(createUniqueSetupProviderId('openai', [])).toBe('openai')
    expect(createUniqueSetupProviderId('openai', [{ id: 'openai' }])).toBe('openai-2')
  })
})

describe('listSetupModels', () => {
  it('passes a draft provider to the backend over stdin', async () => {
    const runBackend = vi.fn().mockResolvedValue({
      kind: 'success',
      models: ['gpt-5.6', 'gpt-5.5']
    })

    const result = await listSetupModels(
      {
        provider: {
          id: 'openai-main',
          displayName: 'OpenAI-Responses',
          protocol: 'openai-responses',
          endPoint: 'https://example.com/v1',
          apiKey: 'test-api-key'
        }
      },
      { runBackend }
    )

    expect(result).toEqual({ kind: 'success', models: ['gpt-5.6', 'gpt-5.5'] })
    expect(runBackend).toHaveBeenCalledWith(
      ['model-catalog', '--stdin'],
      expect.stringContaining('test-api-key'),
      30_000
    )
    expect(runBackend.mock.calls[0][0].join(' ')).not.toContain('test-api-key')
    expect(JSON.parse(runBackend.mock.calls[0][1])).toMatchObject({
      id: 'openai-main',
      protocol: 'openai-responses',
      apiKey: 'test-api-key'
    })
  })

  it('returns the backend auth-required state for ChatGPT setup', async () => {
    const runBackend = vi.fn().mockResolvedValue({ kind: 'auth-required' })

    const result = await listSetupModels(
      {
        provider: {
          id: 'openai-chatgpt',
          displayName: 'OpenAI (ChatGPT)',
          protocol: 'openai-responses',
          endPoint: '',
          apiKey: '',
          authMethod: 'chatgptOAuth'
        }
      },
      { runBackend }
    )

    expect(result).toEqual({ kind: 'auth-required' })
    expect(runBackend).toHaveBeenCalledWith(['model-catalog', '--stdin'], expect.any(String), 30_000)
  })

  it('references existing providers by id without copying credentials', async () => {
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(userHome, '.craft'), { recursive: true })
    writeFileSync(
      userConfigPath,
      JSON.stringify({
        Providers: {
          anthropic: {
            DisplayName: 'Anthropic',
            Protocol: 'anthropic',
            ApiKey: 'sk-inherited',
            EndPoint: 'https://api.anthropic.com'
          }
        }
      }),
      'utf8'
    )
    const runBackend = vi.fn().mockResolvedValue({ kind: 'success', models: ['claude-opus-4-5'] })

    const result = await listSetupModels(
      { providerId: 'anthropic' },
      { userConfigPath, runBackend }
    )

    expect(result).toEqual({ kind: 'success', models: ['claude-opus-4-5'] })
    expect(runBackend).toHaveBeenCalledWith(
      ['model-catalog', '--provider-id', 'anthropic'],
      undefined,
      30_000
    )
  })

  it('preserves backend model error classification', async () => {
    const runBackend = vi.fn().mockResolvedValue({ kind: 'missing-key' })
    const result = await listSetupModels(
      {
        provider: {
          id: 'anthropic',
          displayName: 'Anthropic',
          protocol: 'anthropic',
          endPoint: 'https://api.anthropic.com',
          apiKey: ''
        }
      },
      { runBackend }
    )

    expect(result).toEqual({ kind: 'missing-key' })
  })
})
