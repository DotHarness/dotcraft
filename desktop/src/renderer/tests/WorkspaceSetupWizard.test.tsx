import { describe, expect, it, beforeEach, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { WorkspaceSetupInterstitial } from '../components/WorkspaceSetupInterstitial'
import { WorkspaceSetupWizard } from '../components/WorkspaceSetupWizard'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { WorkspaceStatusPayload } from '../../preload/api.d'
import type { ModelPreference } from '../../shared/modelPreference'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const runSetup = vi.fn()
const listSetupModels = vi.fn()
const loginSetupChatGpt = vi.fn()

function renderWizard(workspaceStatus: WorkspaceStatusPayload, onChooseDifferentWorkspace = vi.fn()) {
  return render(
    <LocaleProvider>
      <WorkspaceSetupWizard
        workspacePath="X:\\fixtures\\workspace"
        workspaceStatus={workspaceStatus}
        onChooseDifferentWorkspace={onChooseDifferentWorkspace}
        onCancel={() => {}}
      />
    </LocaleProvider>
  )
}

function renderInterstitial(isOpening = false, onStart = vi.fn()) {
  return render(
    <LocaleProvider>
      <WorkspaceSetupInterstitial
        workspacePath="X:\\fixtures\\workspace"
        isOpening={isOpening}
        onStart={onStart}
        onChooseDifferentWorkspace={() => {}}
      />
    </LocaleProvider>
  )
}

async function openConfigStep(): Promise<void> {
  fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
  await waitFor(() => {
    const control = screen.getByLabelText('Model')
    expect(['BUTTON', 'INPUT']).toContain(control.tagName)
  })
}

async function findManualModelInput(): Promise<HTMLInputElement> {
  return waitFor(() => {
    const control = screen.getByLabelText('Model')
    expect(control.tagName).toBe('INPUT')
    return control as HTMLInputElement
  })
}

function preference(model: string): ModelPreference {
  return {
    model,
    reasoning: { enabled: false, effort: 'medium', output: 'full' },
    speed: 'standard',
    contextWindow: { mode: 'default' }
  }
}

async function createWorkspace(): Promise<void> {
  fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Create Workspace' }))
  await waitFor(() => {
    expect(runSetup).toHaveBeenCalled()
  })
}

describe('WorkspaceSetupWizard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue(undefined)
    runSetup.mockResolvedValue(undefined)
    listSetupModels.mockResolvedValue({ kind: 'unsupported' })
    loginSetupChatGpt.mockResolvedValue({ kind: 'success' })

    installDesktopApiMock({
      settings: {
        get: settingsGet,
        set: settingsSet
      },
      workspace: {
        listSetupModels,
        loginSetupChatGpt,
        runSetup
      }
    })
  })

  it('shows the interstitial as a short setup wizard entry and disables actions while opening', () => {
    const onStart = vi.fn()
    renderInterstitial(false, onStart)

    expect(screen.getByText("This workspace hasn't finished DotCraft setup")).toBeInTheDocument()
    expect(screen.getByText('Current workspace')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Start workspace setup/ }))
    expect(onStart).toHaveBeenCalledTimes(1)

    renderInterstitial(true, onStart)
    const openingButton = screen.getAllByRole('button', { name: /Start workspace setup/ }).at(-1)!
    expect(openingButton).toBeDisabled()
  })

  it('lets the first wizard step change folders from the read-only workspace card', () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }
    const onChooseDifferentWorkspace = vi.fn()

    renderWizard(status, onChooseDifferentWorkspace)
    fireEvent.click(screen.getByRole('button', { name: 'Change folder' }))

    expect(onChooseDifferentWorkspace).toHaveBeenCalledTimes(1)
  })

  it('allows returning to completed steps from the stepper but keeps future steps locked', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    const currentStep = screen.getByRole('button', { name: 'Confirm workspace' })
    expect(currentStep).toHaveAttribute('aria-current', 'step')
    expect(screen.getByText('Step 1 of 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Configure model provider' })).toBeDisabled()

    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    const previousStep = screen.getByRole('button', { name: 'Confirm workspace' })
    expect(previousStep).not.toBeDisabled()
    expect(screen.getByRole('button', { name: 'Configure model provider' })).toHaveAttribute('aria-current', 'step')
    expect(screen.getByText('Step 2 of 3')).toBeInTheDocument()

    fireEvent.click(previousStep)
    expect(screen.getByText('Confirm DotCraft workspace')).toBeInTheDocument()
  })

  it('selects an existing explicit provider and saves only provider id and model', async () => {
    listSetupModels.mockResolvedValue({
      kind: 'success',
      models: [{ id: 'claude-sonnet-4-5' }]
    })
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: true,
      userConfigDefaults: {
        providerId: 'anthropic',
        model: 'claude-opus-4-5'
      },
      providers: [
        {
          id: 'anthropic',
          displayName: 'Anthropic',
          protocol: 'anthropic',
          hasApiKey: true,
          endPoint: 'https://api.anthropic.com',
          networkTimeoutSeconds: null
        }
      ]
    }

    renderWizard(status)
    await openConfigStep()

    expect(await screen.findByLabelText('Provider')).toHaveValue('anthropic')
    expect(listSetupModels).toHaveBeenCalledWith({ providerId: 'anthropic' })
    expect(await screen.findByLabelText('Model')).toHaveValue('claude-sonnet-4-5')

    await createWorkspace()

    expect(runSetup).toHaveBeenCalledWith({
      model: 'claude-sonnet-4-5',
      preference: preference('claude-sonnet-4-5'),
      providerMode: 'existing',
      providerId: 'anthropic',
      setAsUserDefault: false
    })
  })

  it('offers a detected CLAUDE.md import before provider setup', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
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
          networkTimeoutSeconds: null
        }
      ],
      bootstrapImportSources: [
        {
          id: 'claude',
          fileName: 'CLAUDE.md',
          path: 'X:\\fixtures\\workspace\\CLAUDE.md',
          relativePath: 'CLAUDE.md'
        }
      ]
    }

    renderWizard(status)

    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    expect(screen.getByText('Import existing coding-agent config')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Claude Code/ })).toHaveAttribute('aria-checked', 'true')

    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    await waitFor(() => {
      expect(screen.queryByText('Loading available models...')).not.toBeInTheDocument()
    })
    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))

    expect(screen.getByText('Imported config')).toBeInTheDocument()
    expect(screen.getByText('Claude Code - CLAUDE.md')).toBeInTheDocument()

    fireEvent.click(await screen.findByRole('button', { name: 'Create Workspace' }))
    await waitFor(() => {
      expect(runSetup).toHaveBeenCalled()
    })
    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      bootstrapImportSourceId: 'claude'
    }))
  })

  it('creates an Anthropic template provider with default id anthropic', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    await openConfigStep()

    fireEvent.click(screen.getByRole('button', { name: /Anthropic/ }))
    expect(screen.getByLabelText('API endpoint')).toHaveValue('https://api.anthropic.com')

    const modelInput = await findManualModelInput()
    expect(modelInput).toHaveValue('')
    fireEvent.change(modelInput, { target: { value: 'claude-sonnet-4-5' } })

    await createWorkspace()

    expect(runSetup).toHaveBeenCalledWith({
      model: 'claude-sonnet-4-5',
      preference: preference('claude-sonnet-4-5'),
      providerMode: 'create',
      provider: {
        id: 'anthropic',
        displayName: 'Anthropic',
        protocol: 'anthropic',
        apiKey: '',
        endPoint: 'https://api.anthropic.com',
        networkTimeoutSeconds: null,
        authMethod: 'apiKey'
      },
      setAsUserDefault: true
    })
  })

  it('allows an OpenAI-Responses template provider to keep the endpoint blank', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    await openConfigStep()

    fireEvent.change(screen.getByLabelText('API endpoint'), { target: { value: '' } })
    fireEvent.change(await findManualModelInput(), { target: { value: 'gpt-4.1' } })
    await createWorkspace()

    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      providerMode: 'create',
      provider: expect.objectContaining({
        protocol: 'openai-responses',
        endPoint: ''
      })
    }))
  })

  it('allows a custom OpenAI-Legacy provider to emit the chat completions protocol', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    await openConfigStep()

    fireEvent.click(screen.getByRole('button', { name: /Custom/ }))
    const protocolSelect = await screen.findByRole('combobox', { name: 'Protocol' })
    expect(protocolSelect).toHaveTextContent('OpenAI-Responses')
    fireEvent.click(protocolSelect)
    fireEvent.click(await screen.findByRole('option', { name: 'OpenAI-Legacy' }))
    fireEvent.change(await findManualModelInput(), { target: { value: 'gpt-4.1' } })

    await createWorkspace()

    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      providerMode: 'create',
      provider: expect.objectContaining({
        id: 'provider',
        protocol: 'openai-chat-completions'
      })
    }))
  })

  it('clears chatgptOAuth when a custom provider switches off the Responses protocol', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    await openConfigStep()

    fireEvent.click(screen.getByRole('button', { name: /Custom/ }))

    // Pick ChatGPT subscription on the (default) Responses protocol.
    fireEvent.click(await screen.findByRole('button', { name: /Sign in with ChatGPT/i }))

    // Now move the custom provider off Responses; the OAuth selection must be cleared so the
    // saved payload stays consistent with the new protocol.
    const protocolSelect = await screen.findByRole('combobox', { name: 'Protocol' })
    fireEvent.click(protocolSelect)
    fireEvent.click(await screen.findByRole('option', { name: 'OpenAI-Legacy' }))

    fireEvent.change(await findManualModelInput(), { target: { value: 'gpt-4.1' } })
    await createWorkspace()

    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      provider: expect.objectContaining({
        protocol: 'openai-chat-completions',
        authMethod: 'apiKey'
      })
    }))
  })

  it('logs in for ChatGPT setup and reloads the backend model catalog', async () => {
    let loggedIn = false
    listSetupModels.mockImplementation(async (request) => {
      if (request.provider?.authMethod !== 'chatgptOAuth') return { kind: 'unsupported' }
      return loggedIn
        ? { kind: 'success', models: [{ id: 'gpt-5.6' }, { id: 'gpt-5.5' }] }
        : { kind: 'auth-required' }
    })
    loginSetupChatGpt.mockImplementation(async () => {
      loggedIn = true
      return { kind: 'success' }
    })
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: '/workspace/demo',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    await openConfigStep()
    fireEvent.click(screen.getByRole('button', { name: /Custom/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Sign in with ChatGPT/i }))

    await screen.findAllByRole('button', { name: /Sign in with ChatGPT/i })
    const signInButtons = await screen.findAllByRole('button', { name: /Sign in with ChatGPT/i })
    fireEvent.click(signInButtons.at(-1)!)

    await waitFor(() => expect(loginSetupChatGpt).toHaveBeenCalledWith('provider'))
    await waitFor(() => expect(screen.getByLabelText('Model')).toHaveValue('gpt-5.6'))
  })

  it('falls back to a suffixed Anthropic id when anthropic already exists', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: true,
      providers: [
        {
          id: 'anthropic',
          displayName: 'Anthropic Work',
          protocol: 'anthropic',
          hasApiKey: true,
          endPoint: 'https://api.anthropic.com',
          networkTimeoutSeconds: null
        }
      ]
    }

    renderWizard(status)
    await openConfigStep()

    fireEvent.click(screen.getByRole('button', { name: /Anthropic/ }))
    fireEvent.change(await findManualModelInput(), { target: { value: 'claude-sonnet-4-5' } })
    await createWorkspace()

    expect(runSetup.mock.calls[0][0].provider.id).toBe('anthropic-2')
  })

  it('requires a model and does not expose skip provider setup', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    await openConfigStep()

    expect(screen.queryByRole('button', { name: /Skip for now/ })).not.toBeInTheDocument()
    expect(screen.queryByText('Skip for now')).not.toBeInTheDocument()

    const nextButton = screen.getByRole('button', { name: 'Next' })
    expect(nextButton).toBeDisabled()

    fireEvent.change(await findManualModelInput(), { target: { value: 'gpt-4.1' } })

    expect(nextButton).not.toBeDisabled()
    fireEvent.click(nextButton)
    expect(screen.getByRole('heading', { name: 'Confirm and create' })).toBeInTheDocument()
  })

  it('passes the DotCraft logo to the setup completion handoff', async () => {
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }
    const onRunSetup = vi.fn().mockResolvedValue(undefined)

    render(
      <LocaleProvider>
        <WorkspaceSetupWizard
          workspacePath="X:\\fixtures\\workspace"
          workspaceStatus={status}
          onRunSetup={onRunSetup}
          onChooseDifferentWorkspace={() => {}}
          onCancel={() => {}}
        />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    await waitFor(() => {
      expect(screen.queryByText('Loading available models...')).not.toBeInTheDocument()
    })
    fireEvent.change(await findManualModelInput(), { target: { value: 'gpt-4.1' } })
    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Create Workspace' }))

    await waitFor(() => {
      expect(onRunSetup).toHaveBeenCalled()
    })
    expect(onRunSetup.mock.calls[0][0]).toEqual(expect.objectContaining({
      providerMode: 'create',
      model: 'gpt-4.1',
      provider: expect.objectContaining({
        id: 'openai',
        protocol: 'openai-responses'
      })
    }))
    expect(decodeURIComponent(onRunSetup.mock.calls[0][1].logoSrc)).toContain("<title id='title'>DotCraft</title>")
    expect(onRunSetup.mock.calls[0][1].logoRect).toEqual(expect.objectContaining({
      width: expect.any(Number),
      height: expect.any(Number)
    }))
    expect(runSetup).not.toHaveBeenCalled()
  })

  it('keeps manual model entry available when model list is unavailable', async () => {
    listSetupModels.mockResolvedValue({ kind: 'missing-key' })
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: 'X:\\fixtures\\workspace',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    await openConfigStep()

    const modelControl = await waitFor(() => {
      const control = screen.getByLabelText('Model')
      expect(control.tagName).toBe('INPUT')
      return control
    })
    expect(modelControl).toHaveValue('')
    expect(screen.getByText('Model list unavailable. Enter a model manually.')).toBeInTheDocument()
  })

  it('ends loading after a model catalog rejection and retries successfully', async () => {
    listSetupModels
      .mockRejectedValueOnce(new Error('backend failed'))
      .mockResolvedValueOnce({ kind: 'success', models: [{ id: 'gpt-5.6' }, { id: 'gpt-5.5' }] })
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: '/workspace/demo',
      hasUserConfig: false,
      providers: []
    }

    renderWizard(status)
    await openConfigStep()

    expect(screen.getByLabelText('Model')).toBeInTheDocument()
    expect(screen.queryByText('Loading available models...')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => expect(listSetupModels).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.getByLabelText('Model')).toHaveValue('gpt-5.6'))
  })

  it('ignores a rejected model request after switching providers', async () => {
    let rejectExisting!: (error: Error) => void
    listSetupModels.mockImplementation((request) => {
      if ('providerId' in request) {
        return new Promise((_resolve, reject) => {
          rejectExisting = reject
        })
      }
      return Promise.resolve({ kind: 'success', models: [{ id: 'claude-sonnet-4-5' }] })
    })
    const status: WorkspaceStatusPayload = {
      status: 'needs-setup',
      workspacePath: '/workspace/demo',
      hasUserConfig: true,
      providers: [{
        id: 'existing-provider',
        displayName: 'Existing Provider',
        protocol: 'openai-responses',
        hasApiKey: true,
        endPoint: 'https://example.invalid/v1',
        networkTimeoutSeconds: null
      }]
    }

    renderWizard(status)
    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    await waitFor(() => expect(listSetupModels).toHaveBeenCalledWith({ providerId: 'existing-provider' }))

    fireEvent.click(screen.getByRole('button', { name: /Anthropic/ }))
    await waitFor(() => expect(screen.getByLabelText('Model')).toHaveValue('claude-sonnet-4-5'))
    rejectExisting(new Error('stale request failed'))

    await waitFor(() => expect(screen.getByLabelText('Model')).toHaveValue('claude-sonnet-4-5'))
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })
})
