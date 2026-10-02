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
        promptSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        skillsIncludeSharedSkills: null,
        memoryEnabled: null,
        dreamsEnabled: null,
        dreamsInterval: null,
        dreamsThreadLookbackCount: null,
        dreamsAutoApply: null,
        defaultApprovalPolicy: null,
        toolsImageGenerationEnabled: null,
        toolsImageGenerationProvider: null,
        toolsCodeModeMode: null
      },
      userDefaults: {
        providerId: null,
        providerPreferences: {},
        welcomeSuggestionsEnabled: null,
        promptSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        skillsIncludeSharedSkills: null,
        memoryEnabled: null,
        dreamsEnabled: null,
        dreamsInterval: null,
        dreamsThreadLookbackCount: null,
        dreamsAutoApply: null,
        defaultApprovalPolicy: null,
        toolsImageGenerationEnabled: null,
        toolsImageGenerationProvider: null,
        toolsCodeModeMode: null
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
        memoryEnabled: false,
        dreamsEnabled: false,
        dreamsInterval: '1.00:00:00',
        dreamsThreadLookbackCount: 50,
        dreamsAutoApply: true,
        defaultApprovalPolicy: 'autoApprove',
        toolsCodeModeMode: 'only'
      },
      userDefaults: {
        skillsSelfLearningEnabled: false,
        memoryEnabled: true,
        dreamsEnabled: true,
        dreamsInterval: '12:00:00',
        dreamsThreadLookbackCount: 20,
        dreamsAutoApply: false,
        defaultApprovalPolicy: 'default',
        toolsCodeModeMode: 'sometimes'
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
        promptSuggestionsEnabled: null,
        skillsSelfLearningEnabled: true,
        skillsIncludeSharedSkills: null,
        memoryEnabled: false,
        dreamsEnabled: false,
        dreamsInterval: '24:00:00',
        dreamsThreadLookbackCount: 50,
        dreamsAutoApply: true,
        defaultApprovalPolicy: 'autoApprove',
        toolsImageGenerationEnabled: null,
        toolsImageGenerationProvider: null,
        toolsCodeModeMode: 'only'
      },
      userDefaults: {
        providerId: null,
        providerPreferences: {},
        welcomeSuggestionsEnabled: null,
        promptSuggestionsEnabled: null,
        skillsSelfLearningEnabled: false,
        skillsIncludeSharedSkills: null,
        memoryEnabled: true,
        dreamsEnabled: true,
        dreamsInterval: '12:00:00',
        dreamsThreadLookbackCount: 20,
        dreamsAutoApply: false,
        defaultApprovalPolicy: 'default',
        toolsImageGenerationEnabled: null,
        toolsImageGenerationProvider: null,
        toolsCodeModeMode: null
      }
    })
  })
})
