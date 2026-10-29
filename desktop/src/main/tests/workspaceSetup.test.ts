import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import {
  createUniqueSetupProviderId,
  getWorkspaceStatus,
  listSetupModels,
  shouldRouteWorkspaceThroughSetupBeforeAppServerStart
} from '../workspaceSetup'

const tempDirs: string[] = []
const preference = (model: string) => ({
  model,
  reasoning: { enabled: false, effort: 'medium', output: 'full' },
  speed: 'standard'
})

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
  it('returns no-workspace for empty paths and keeps empty workspace config in setup until a provider is configured', () => {
    const userConfigPath = join(createTempWorkspace(), '.craft', 'config.json')
    expect(getWorkspaceStatus('', { userConfigPath })).toEqual({
      status: 'no-workspace',
      workspacePath: '',
      hasUserConfig: false,
      providers: []
    })

    const workspace = createTempWorkspace()
    writeJson(join(workspace, '.craft', 'config.json'), {})
    expect(getWorkspaceStatus(workspace, { userConfigPath })).toEqual({
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
      ProviderPreferences: { openai: preference('gpt-4.1') },
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
        model: 'gpt-4.1',
        preference: preference('gpt-4.1')
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

  it('treats a folder without workspace config as ready once personal defaults resolve', () => {
    const workspace = createTempWorkspace()
    const userConfigPath = join(createTempWorkspace(), '.craft', 'config.json')
    expect(getWorkspaceStatus(workspace, { userConfigPath })).toMatchObject({ status: 'needs-setup' })

    writeJson(userConfigPath, {
      ProviderId: 'openai',
      ProviderPreferences: { openai: preference('gpt-4.1') },
      Providers: { openai: { DisplayName: 'OpenAI', Protocol: 'openai-responses', ApiKey: 'sk-test' } }
    })
    expect(getWorkspaceStatus(workspace, { userConfigPath })).toMatchObject({ status: 'ready' })
    expect(shouldRouteWorkspaceThroughSetupBeforeAppServerStart(workspace, { userConfigPath })).toBe(false)
  })

  it('is ready only when the workspace selects a provider configured in user config', () => {
    const workspace = createTempWorkspace()
    const userConfigPath = join(createTempWorkspace(), '.craft', 'config.json')
    writeJson(userConfigPath, {
      ProviderPreferences: { anthropic: preference('claude-sonnet-4-5') },
      Providers: {
        anthropic: {
          DisplayName: 'Anthropic',
          Protocol: 'anthropic',
          ApiKey: 'sk-ant',
          EndPoint: 'https://api.anthropic.com'
        }
      }
    })

    writeJson(join(workspace, '.craft', 'config.json'), { ProviderId: 'anthropic' })
    expect(getWorkspaceStatus(workspace, { userConfigPath })).toMatchObject({
      status: 'ready',
      hasUserConfig: true,
      providers: [expect.objectContaining({ id: 'anthropic', protocol: 'anthropic' })]
    })

    writeJson(join(workspace, '.craft', 'config.json'), { ProviderId: 'missing' })
    expect(getWorkspaceStatus(workspace, { userConfigPath })).toMatchObject({
      status: 'needs-setup',
      hasUserConfig: true
    })
  })

  it('returns explicit user providers', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(userHome, '.craft'), { recursive: true })
    writeFileSync(
      userConfigPath,
      JSON.stringify({
        ProviderId: 'anthropic',
        ProviderPreferences: { anthropic: preference('claude-sonnet-4-5') },
        Providers: {
          openai: {
            DisplayName: 'OpenAI',
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
      status: 'ready',
      workspacePath: workspace,
      hasUserConfig: true,
      userConfigDefaults: {
        providerId: 'anthropic',
        model: 'claude-sonnet-4-5',
        preference: preference('claude-sonnet-4-5')
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
          displayName: 'OpenAI',
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
  it('routes only local setup-required workspaces through setup before AppServer start', () => {
    const workspace = createTempWorkspace()
    const userConfigPath = join(createTempWorkspace(), '.craft', 'config.json')
    writeJson(join(workspace, '.craft', 'config.json'), {})

    expect(shouldRouteWorkspaceThroughSetupBeforeAppServerStart(workspace, { userConfigPath })).toBe(true)
    expect(shouldRouteWorkspaceThroughSetupBeforeAppServerStart(workspace, {
      usingRemoteConnection: true,
      userConfigPath
    })).toBe(false)

    writeJson(join(workspace, '.craft', 'config.json'), {
      ProviderId: 'openai',
      ProviderPreferences: { openai: preference('gpt-4.1') }
    })
    writeJson(userConfigPath, {
      ProviderPreferences: { openai: preference('gpt-4.1') },
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

    expect(result).toEqual({
      kind: 'success',
      models: [{ id: 'gpt-5.6' }, { id: 'gpt-5.5' }]
    })
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

  it.each([
    [{ kind: 'auth-required' }, { id: 'openai-chatgpt', displayName: 'OpenAI (ChatGPT)', protocol: 'openai-responses', endPoint: '', apiKey: '', authMethod: 'chatgptOAuth' }],
    [{ kind: 'missing-key' }, { id: 'anthropic', displayName: 'Anthropic', protocol: 'anthropic', endPoint: 'https://api.anthropic.com', apiKey: '' }]
  ] as const)('preserves the backend %o classification', async (backendResult, provider) => {
    const runBackend = vi.fn().mockResolvedValue(backendResult)

    expect(await listSetupModels({ provider }, { runBackend })).toEqual(backendResult)
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

    expect(result).toEqual({
      kind: 'success',
      models: [{ id: 'claude-opus-4-5' }]
    })
    expect(runBackend).toHaveBeenCalledWith(
      ['model-catalog', '--provider-id', 'anthropic'],
      undefined,
      30_000
    )
  })
})
