import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { useConnectionStore } from '../stores/connectionStore'
import { usePendingRestartStore } from '../stores/pendingRestartStore'
import { chooseValueIn } from './selectHarness'

const settingsGet = vi.fn()
const settingsSet = vi.fn()

function renderView(): void {
  render(
    <LocaleProvider>
      <SettingsView workspacePath="E:\\Git\\dotcraft" />
    </LocaleProvider>
  )
}

describe('SettingsView notification settings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    usePendingRestartStore.getState().clear()

    settingsGet.mockResolvedValue({
      locale: 'en',
      connectionMode: 'local',
      notifications: {
        taskCompletionMode: 'whenUnfocused'
      }
    })
    settingsSet.mockResolvedValue(undefined)

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        workspaceConfig: {
          getCore: vi.fn().mockResolvedValue({
            workspace: {
              apiKey: null,
              endPoint: null,
              welcomeSuggestionsEnabled: null,
              skillsSelfLearningEnabled: null,
              memoryAutoConsolidateEnabled: null,
              defaultApprovalPolicy: 'default'
            },
            userDefaults: {
              apiKey: null,
              endPoint: null,
              welcomeSuggestionsEnabled: null,
              skillsSelfLearningEnabled: null,
              memoryAutoConsolidateEnabled: null,
              defaultApprovalPolicy: null
            }
          })
        },
        appServer: {
          sendRequest: vi.fn(async (method: string) => {
            if (method === 'channel/list') return { channels: [] }
            return {}
          }),
          restartManaged: vi.fn(),
          getResolvedBinary: vi.fn().mockResolvedValue({ path: null }),
          pickBinary: vi.fn()
        },
        modules: { list: vi.fn().mockResolvedValue([]) },
        workspace: {
          pickFolder: vi.fn(),
          viewer: { browserUse: { clearCookies: vi.fn() } }
        },
        shell: { openExternal: vi.fn() }
      }
    })

    useConnectionStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true
      }
    })
  })

  it('loads and persists the task completion notification mode', async () => {
    renderView()

    const select = await screen.findByRole('combobox', { name: 'Task completion notifications' })
    expect(select).toHaveValue('whenUnfocused')

    await chooseValueIn(select, 'never')

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({
        notifications: {
          taskCompletionMode: 'never'
        }
      })
    })
    expect(select).toHaveValue('never')
  })
})
