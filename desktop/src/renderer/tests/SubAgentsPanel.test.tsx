import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SubAgentsPanel } from '../components/settings/panels/SubAgentsPanel'
import { useModelCatalogStore } from '../stores/modelCatalogStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const appServerListModels = vi.fn()

const codexBuiltIn = {
  runtime: 'cli-oneshot',
  bin: 'codex',
  args: ['exec'],
  workingDirectoryMode: 'workspace',
  inputMode: 'arg',
  outputFormat: 'text',
  outputFileArgTemplate: '--output-last-message {path}',
  readOutputFile: true,
  deleteOutputFileAfterRead: true,
  supportsStreaming: false,
  supportsResume: true,
  resumeArgTemplate: 'resume {sessionId}',
  resumeSessionIdRegex: '"thread_id"\\s*:\\s*"(?<sessionId>[^"]+)"',
  timeout: 300,
  maxOutputBytes: 1048576,
  trustLevel: 'prompt',
  permissionModeMapping: {
    interactive: '--sandbox workspace-write',
    'auto-approve': '--sandbox workspace-write --ask-for-approval never',
    restricted: '--sandbox read-only'
  }
}

const cursorBuiltIn = {
  runtime: 'cli-oneshot',
  bin: 'cursor-agent',
  args: ['--print'],
  workingDirectoryMode: 'workspace',
  inputMode: 'arg',
  outputFormat: 'json',
  outputJsonPath: 'result',
  supportsStreaming: false,
  supportsResume: true,
  resumeArgTemplate: '--resume {sessionId}',
  resumeSessionIdJsonPath: 'session_id',
  timeout: 300,
  maxOutputBytes: 1048576,
  trustLevel: 'prompt'
}

const baseList = {
  defaultName: 'native',
  settings: {
    externalCliSessionResumeEnabled: false
  },
  profiles: [
    {
      name: 'native',
      isBuiltIn: true,
      isTemplate: false,
      hasWorkspaceOverride: false,
      isDefault: true,
      enabled: true,
      definition: {
        runtime: 'native',
        workingDirectoryMode: 'workspace',
        trustLevel: 'trusted'
      },
      builtInDefaults: {
        runtime: 'native',
        workingDirectoryMode: 'workspace',
        trustLevel: 'trusted'
      },
      diagnostic: {
        enabled: true,
        binaryResolved: true,
        hiddenFromPrompt: false,
        hiddenReason: null,
        warnings: []
      }
    },
    {
      name: 'codex-cli',
      isBuiltIn: true,
      isTemplate: false,
      hasWorkspaceOverride: false,
      isDefault: false,
      enabled: true,
      definition: { ...codexBuiltIn },
      builtInDefaults: { ...codexBuiltIn },
      diagnostic: {
        enabled: true,
        binaryResolved: true,
        hiddenFromPrompt: false,
        hiddenReason: null,
        warnings: []
      }
    },
    {
      name: 'cursor-cli',
      isBuiltIn: true,
      isTemplate: false,
      hasWorkspaceOverride: false,
      isDefault: false,
      enabled: false,
      definition: { ...cursorBuiltIn },
      builtInDefaults: { ...cursorBuiltIn },
      diagnostic: {
        enabled: false,
        binaryResolved: false,
        hiddenFromPrompt: false,
        hiddenReason: null,
        warnings: []
      }
    },
    {
      name: 'custom-cli-oneshot',
      isBuiltIn: true,
      isTemplate: true,
      hasWorkspaceOverride: false,
      isDefault: false,
      enabled: true,
      definition: { ...codexBuiltIn },
      builtInDefaults: null,
      diagnostic: {
        enabled: true,
        binaryResolved: true,
        hiddenFromPrompt: true,
        hiddenReason: 'template profile',
        warnings: []
      }
    },
    {
      name: 'my-runner',
      isBuiltIn: false,
      isTemplate: false,
      hasWorkspaceOverride: false,
      isDefault: false,
      enabled: true,
      definition: {
        runtime: 'cli-oneshot',
        bin: 'my-runner',
        args: ['run'],
        workingDirectoryMode: 'workspace',
        inputMode: 'arg',
        outputFormat: 'text',
        readOutputFile: false,
        deleteOutputFileAfterRead: false,
        timeout: 600,
        maxOutputBytes: 1048576,
        trustLevel: 'prompt'
      },
      builtInDefaults: null,
      diagnostic: {
        enabled: true,
        binaryResolved: true,
        hiddenFromPrompt: false,
        hiddenReason: null,
        warnings: []
      }
    }
  ]
}

function cloneList(): typeof baseList {
  return JSON.parse(JSON.stringify(baseList)) as typeof baseList
}

function renderPanel(refreshTick = 0): void {
  render(
    <LocaleProvider>
      <SubAgentsPanel enabled={true} refreshTick={refreshTick} />
    </LocaleProvider>
  )
}

describe('SubAgentsPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useModelCatalogStore.getState().reset()
    settingsGet.mockResolvedValue({})
    appServerListModels.mockResolvedValue({
      success: true,
      models: [{ id: 'gpt-subagent' }, { id: 'gpt-main' }]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'subagent/profiles/list') {
        return cloneList()
      }
      if (method === 'subagent/profiles/upsert') {
        return { profile: cloneList().profiles[1] }
      }
      if (method === 'subagent/profiles/setEnabled') {
        return { profile: cloneList().profiles[1] }
      }
      if (method === 'subagent/settings/update') {
        return { settings: { externalCliSessionResumeEnabled: true } }
      }
      return { removed: true }
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest, listModels: appServerListModels },
        workspace: { saveImageToTemp: vi.fn() }
      }
    })
  })

  it('shows preset and custom list cards and hides the template', async () => {
    renderPanel()

    expect(
      await screen.findByRole('button', { name: 'Open sub-agent profile native' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Open sub-agent profile codex-cli' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Open sub-agent profile cursor-cli' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Open sub-agent profile my-runner' })
    ).toBeInTheDocument()

    expect(
      screen.queryByRole('button', { name: 'Open sub-agent profile custom-cli-oneshot' })
    ).not.toBeInTheDocument()

    expect(screen.getByRole('switch', { name: 'Toggle sub-agent native' })).toBeDisabled()
    expect(screen.getByRole('switch', { name: 'Toggle sub-agent codex-cli' })).not.toBeDisabled()
  })

  it('updates the workspace resume switch', async () => {
    renderPanel()

    fireEvent.click(await screen.findByRole('switch', { name: 'Resume external CLI sessions' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'subagent/settings/update',
        expect.objectContaining({ externalCliSessionResumeEnabled: true })
      )
    })
  })

  it('navigates from the codex card into the preset detail view and back', async () => {
    renderPanel()

    fireEvent.click(
      await screen.findByRole('button', { name: 'Open sub-agent profile codex-cli' })
    )

    expect(await screen.findByText('Codex CLI')).toBeInTheDocument()
    expect(screen.getByText('Binary found')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Customize' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    expect(
      await screen.findByRole('button', { name: 'Open sub-agent profile native' })
    ).toBeInTheDocument()
  })

  it('opens the customize panel and saves an override that appends extra args', async () => {
    renderPanel()

    fireEvent.click(
      await screen.findByRole('button', { name: 'Open sub-agent profile codex-cli' })
    )
    fireEvent.click(await screen.findByRole('button', { name: 'Customize' }))

    const timeoutInput = screen.getByTestId('subagent-preset-timeout-input')
    fireEvent.change(timeoutInput, { target: { value: '450' } })

    const extraArgInput = screen.getByPlaceholderText('--profile my-profile')
    fireEvent.change(extraArgInput, { target: { value: '--profile custom' } })

    fireEvent.click(screen.getByRole('button', { name: 'Save overrides' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'subagent/profiles/upsert',
        expect.objectContaining({
          name: 'codex-cli',
          definition: expect.objectContaining({
            bin: 'codex',
            args: ['exec', '--profile custom'],
            timeout: 450
          })
        })
      )
    })
  })

  it('opens the native detail view with a locked switch', async () => {
    renderPanel()

    fireEvent.click(
      await screen.findByRole('button', { name: 'Open sub-agent profile native' })
    )

    expect(await screen.findByText('Native')).toBeInTheDocument()
    const switches = await screen.findAllByRole('switch', { name: 'Toggle sub-agent native' })
    for (const toggle of switches) {
      expect(toggle).toBeDisabled()
    }
  })

  it('saves the native model from the model dropdown', async () => {
    renderPanel()

    fireEvent.click(
      await screen.findByRole('button', { name: 'Open sub-agent profile native' })
    )

    const modelSelect = await screen.findByLabelText('Native model')
    fireEvent.change(modelSelect, { target: { value: 'gpt-subagent' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'subagent/settings/update',
        expect.objectContaining({ model: 'gpt-subagent' })
      )
    })
  })

  it('falls back to manual native model entry when model listing is unsupported', async () => {
    appServerListModels.mockResolvedValue({
      success: false,
      errorCode: 'EndpointNotSupported',
      models: []
    })
    renderPanel()

    fireEvent.click(
      await screen.findByRole('button', { name: 'Open sub-agent profile native' })
    )

    const modelInput = await screen.findByPlaceholderText('Inherit MainAgent model')
    fireEvent.change(modelInput, { target: { value: 'manual-subagent-model' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'subagent/settings/update',
        expect.objectContaining({ model: 'manual-subagent-model' })
      )
    })
  })

  it('creates a custom agent through the dedicated add flow', async () => {
    renderPanel()

    await screen.findByRole('button', { name: 'Open sub-agent profile native' })
    fireEvent.click(screen.getByRole('button', { name: 'Add custom agent' }))

    expect(await screen.findByText('New custom agent')).toBeInTheDocument()

    fireEvent.change(screen.getByTestId('subagent-name-input'), {
      target: { value: 'my-helper' }
    })
    fireEvent.change(screen.getByTestId('subagent-bin-input'), {
      target: { value: '/usr/bin/my-helper' }
    })

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'subagent/profiles/upsert',
        expect.objectContaining({
          name: 'my-helper',
          definition: expect.objectContaining({
            runtime: 'cli-oneshot',
            bin: '/usr/bin/my-helper'
          })
        })
      )
    })
  })

  it('toggles enable state directly from the list card', async () => {
    renderPanel()

    const codexSwitch = await screen.findByRole('switch', {
      name: 'Toggle sub-agent codex-cli'
    })
    fireEvent.click(codexSwitch)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'subagent/profiles/setEnabled',
        expect.objectContaining({ name: 'codex-cli', enabled: false })
      )
    })
  })

  it('refreshes when refreshTick changes', async () => {
    const { rerender } = render(
      <LocaleProvider>
        <SubAgentsPanel enabled={true} refreshTick={0} />
      </LocaleProvider>
    )

    await screen.findByRole('button', { name: 'Open sub-agent profile native' })
    const initialListCalls = appServerSendRequest.mock.calls.filter(
      (call) => call[0] === 'subagent/profiles/list'
    ).length
    expect(initialListCalls).toBeGreaterThanOrEqual(1)

    rerender(
      <LocaleProvider>
        <SubAgentsPanel enabled={true} refreshTick={1} />
      </LocaleProvider>
    )

    await waitFor(() => {
      const listCalls = appServerSendRequest.mock.calls.filter(
        (call) => call[0] === 'subagent/profiles/list'
      )
      expect(listCalls.length).toBeGreaterThan(initialListCalls)
    })
  })
})
