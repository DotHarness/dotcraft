import { describe, expect, it } from 'vitest'
import {
  DEFAULT_INITIAL_WORKSPACE_STATUS,
  encodeInitialWorkspaceStatusArg,
  readInitialWorkspaceStatusFromArgv,
  type InitialWorkspaceStatusPayload
} from '../../shared/initialWorkspaceStatus'

describe('initial workspace status argument parsing', () => {
  it('round-trips an encoded ready workspace status', () => {
    const status: InitialWorkspaceStatusPayload = {
      status: 'ready',
      workspacePath: 'C:\\sample\\workspace',
      hasUserConfig: true,
      userConfigDefaults: {
        providerId: 'openai',
        model: 'gpt-5.4'
      },
      providers: [
        {
          id: 'openai',
          displayName: 'OpenAI-Responses',
          protocol: 'openai-responses',
          hasApiKey: true,
          endPoint: 'https://api.openai.com/v1',
          networkTimeoutSeconds: 90
        }
      ]
    }

    expect(readInitialWorkspaceStatusFromArgv(['electron', encodeInitialWorkspaceStatusArg(status)])).toEqual(status)
  })

  it('round-trips remote workspace metadata from startup payloads', () => {
    const status: InitialWorkspaceStatusPayload = {
      status: 'ready',
      workspacePath: 'C:\\sample\\workspace',
      hasUserConfig: true,
      providers: [],
      remote: {
        hostId: 'h1',
        stackId: 's1',
        serverName: 'Example Remote',
        stackName: 'demo-stack',
        workspaceDir: '/srv/sample/demo-stack/deploy/workspace',
        appServerWorkspacePath: '/workspace',
        composeDir: '/srv/sample/demo-stack/deploy',
        projectName: 'deploy'
      }
    }

    expect(readInitialWorkspaceStatusFromArgv(['electron', encodeInitialWorkspaceStatusArg(status)])).toEqual(status)
  })

  it('normalizes legacy OpenAI provider protocols from raw startup payloads', () => {
    const rawStatus = {
      status: 'ready',
      workspacePath: 'C:\\sample\\workspace',
      hasUserConfig: true,
      providers: [
        {
          id: 'openai',
          displayName: 'OpenAI',
          protocol: 'openai',
          hasApiKey: true,
          endPoint: 'https://api.openai.com/v1'
        }
      ]
    }
    const arg = `--dotcraft-initial-workspace-status=${encodeURIComponent(JSON.stringify(rawStatus))}`

    expect(readInitialWorkspaceStatusFromArgv(['electron', arg])).toEqual({
      ...rawStatus,
      providers: [
        {
          id: 'openai',
          displayName: 'OpenAI',
          protocol: 'openai-chat-completions',
          hasApiKey: true,
          endPoint: 'https://api.openai.com/v1'
        }
      ]
    })
  })

  it('falls back to no-workspace when the argument is missing or malformed', () => {
    expect(readInitialWorkspaceStatusFromArgv(['electron'])).toEqual(DEFAULT_INITIAL_WORKSPACE_STATUS)
    expect(readInitialWorkspaceStatusFromArgv(['electron', '--dotcraft-initial-workspace-status=%E0%A4%A'])).toEqual(
      DEFAULT_INITIAL_WORKSPACE_STATUS
    )
    expect(
      readInitialWorkspaceStatusFromArgv([
        'electron',
        `--dotcraft-initial-workspace-status=${encodeURIComponent(JSON.stringify({ status: 'ready' }))}`
      ])
    ).toEqual(DEFAULT_INITIAL_WORKSPACE_STATUS)
  })
})
