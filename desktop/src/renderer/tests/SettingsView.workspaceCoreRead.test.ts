import { describe, expect, it, vi } from 'vitest'
import {
  readWorkspaceCoreSafeFromApi,
  readWorkspaceCoreStrictFromApi
} from '../components/settings/SettingsView'

describe('SettingsView workspace core readers', () => {
  it('returns empty workspace core from safe reader when api is unavailable', async () => {
    await expect(readWorkspaceCoreSafeFromApi(undefined)).resolves.toEqual({
      workspace: {
        providerId: null,
        providerPreferences: {},
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        dreamsEnabled: null,
        dreamsInterval: null,
        dreamsThreadLookbackCount: null,
        dreamsAutoApply: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        providerId: null,
        providerPreferences: {},
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        dreamsEnabled: null,
        dreamsInterval: null,
        dreamsThreadLookbackCount: null,
        dreamsAutoApply: null,
        defaultApprovalPolicy: null
      }
    })
  })

  it('throws from strict reader when api is unavailable', async () => {
    await expect(readWorkspaceCoreStrictFromApi(undefined)).rejects.toThrow(
      'Workspace core API is unavailable'
    )
  })

  it('throws from strict reader when getCore fails', async () => {
    const getCore = vi.fn<() => Promise<unknown>>().mockRejectedValue(new Error('boom'))

    await expect(
      readWorkspaceCoreStrictFromApi({
        workspaceConfig: { getCore }
      })
    ).rejects.toThrow('boom')
  })

  it('normalizes personalization config from workspace and user defaults', async () => {
    const getCore = vi.fn<() => Promise<unknown>>().mockResolvedValue({
      workspace: {
        skillsSelfLearningEnabled: true,
        memoryAutoConsolidateEnabled: false,
        dreamsEnabled: false,
        dreamsInterval: '1.00:00:00',
        dreamsThreadLookbackCount: 50,
        dreamsAutoApply: true,
        defaultApprovalPolicy: 'autoApprove'
      },
      userDefaults: {
        skillsSelfLearningEnabled: false,
        memoryAutoConsolidateEnabled: true,
        dreamsEnabled: true,
        dreamsInterval: '12:00:00',
        dreamsThreadLookbackCount: 20,
        dreamsAutoApply: false,
        defaultApprovalPolicy: 'default'
      }
    })

    await expect(
      readWorkspaceCoreStrictFromApi({
        workspaceConfig: { getCore }
      })
    ).resolves.toEqual({
      workspace: {
        providerId: null,
        providerPreferences: {},
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: true,
        memoryAutoConsolidateEnabled: false,
        dreamsEnabled: false,
        dreamsInterval: '24:00:00',
        dreamsThreadLookbackCount: 50,
        dreamsAutoApply: true,
        defaultApprovalPolicy: 'autoApprove'
      },
      userDefaults: {
        providerId: null,
        providerPreferences: {},
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: false,
        memoryAutoConsolidateEnabled: true,
        dreamsEnabled: true,
        dreamsInterval: '12:00:00',
        dreamsThreadLookbackCount: 20,
        dreamsAutoApply: false,
        defaultApprovalPolicy: 'default'
      }
    })
  })
})
