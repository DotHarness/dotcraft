import { afterEach, describe, expect, it, vi } from 'vitest'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import {
  applyWorkspaceSetupBootstrapImport,
  createUniqueSetupProviderId,
  detectWorkspaceSetupBootstrapImportSources,
  getWorkspaceStatus,
  listSetupModels
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
      Model: 'gpt-4.1',
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

  it('returns needs-setup when model is explicitly empty', () => {
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
        Model: 'claude-sonnet-4-5',
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
    writeFileSync(join(workspace, '.craft', 'config.json'), '{"ProviderId":"openai"}', 'utf8')
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
  it('lists models for an OpenAI Responses draft provider', async () => {
    const fetchImpl = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ data: [{ id: 'gpt-4.1' }, { id: 'deepseek-chat' }] })
    })

    const result = await listSetupModels(
      {
        provider: {
          id: 'openai-main',
          displayName: 'OpenAI-Responses',
          protocol: 'openai-responses',
          endPoint: 'https://example.com/v1',
          apiKey: 'sk-explicit'
        }
      },
      { fetchImpl: fetchImpl as unknown as typeof fetch }
    )

    expect(result).toEqual({ kind: 'success', models: ['deepseek-chat', 'gpt-4.1'] })
    expect(fetchImpl).toHaveBeenCalledWith(
      'https://example.com/v1/models',
      expect.objectContaining({
        method: 'GET',
        headers: expect.objectContaining({
          Authorization: 'Bearer sk-explicit'
        })
      })
    )
  })

  it('uses the official OpenAI endpoint when a draft endpoint is blank', async () => {
    const fetchImpl = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ data: [{ id: 'gpt-4.1' }] })
    })

    const result = await listSetupModels(
      {
        provider: {
          id: 'openai-api',
          displayName: 'OpenAI-Legacy',
          protocol: 'openai-chat-completions',
          endPoint: '',
          apiKey: 'sk-explicit'
        }
      },
      { fetchImpl: fetchImpl as unknown as typeof fetch }
    )

    expect(result).toEqual({ kind: 'success', models: ['gpt-4.1'] })
    expect(fetchImpl).toHaveBeenCalledWith(
      'https://api.openai.com/v1/models',
      expect.objectContaining({
        method: 'GET'
      })
    )
  })

  it('lists models for an Anthropic draft provider', async () => {
    const fetchImpl = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ data: [{ id: 'claude-sonnet-4-5' }] })
    })

    const result = await listSetupModels(
      {
        provider: {
          id: 'anthropic',
          displayName: 'Anthropic',
          protocol: 'anthropic',
          endPoint: 'https://api.anthropic.com',
          apiKey: 'sk-ant'
        }
      },
      { fetchImpl: fetchImpl as unknown as typeof fetch }
    )

    expect(result).toEqual({ kind: 'success', models: ['claude-sonnet-4-5'] })
    expect(fetchImpl).toHaveBeenCalledWith(
      'https://api.anthropic.com/v1/models?limit=1000',
      expect.objectContaining({
        method: 'GET',
        headers: expect.objectContaining({
          'x-api-key': 'sk-ant',
          'anthropic-version': '2023-06-01'
        })
      })
    )
  })

  it('uses bundled ChatGPT fallback models for ChatGPT OAuth setup', async () => {
    const fetchImpl = vi.fn()

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
      { fetchImpl: fetchImpl as unknown as typeof fetch }
    )

    expect(result).toEqual({
      kind: 'success',
      models: ['gpt-5.5', 'gpt-5.4', 'gpt-5.4-mini', 'gpt-5.3-codex', 'gpt-5.2']
    })
    expect(fetchImpl).not.toHaveBeenCalled()
  })

  it('uses stored provider credentials for existing providers', async () => {
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
    const fetchImpl = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ data: [{ id: 'claude-opus-4-5' }] })
    })

    const result = await listSetupModels(
      { providerId: 'anthropic' },
      { userConfigPath, fetchImpl: fetchImpl as unknown as typeof fetch }
    )

    expect(result).toEqual({ kind: 'success', models: ['claude-opus-4-5'] })
    expect(fetchImpl).toHaveBeenCalledWith(
      'https://api.anthropic.com/v1/models?limit=1000',
      expect.objectContaining({
        headers: expect.objectContaining({
          'x-api-key': 'sk-inherited'
        })
      })
    )
  })

  it('returns missing-key when provider has no key', async () => {
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
      { fetchImpl: vi.fn() as unknown as typeof fetch }
    )

    expect(result).toEqual({ kind: 'missing-key' })
  })
})
