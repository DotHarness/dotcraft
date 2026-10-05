import { describe, expect, it, beforeEach, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { WorkspaceSetupInterstitial } from '../components/WorkspaceSetupInterstitial'
import { WorkspaceSetupWizard } from '../components/WorkspaceSetupWizard'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { WorkspaceSetupProviderSummary, WorkspaceStatusPayload } from '../../preload/api.d'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const runSetup = vi.fn()
const listSetupModels = vi.fn()
const loginSetupChatGpt = vi.fn()

const anthropicProvider: WorkspaceSetupProviderSummary = {
  id: 'anthropic',
  displayName: 'Anthropic',
  protocol: 'anthropic',
  hasApiKey: true,
  endPoint: 'https://api.anthropic.com',
  networkTimeoutSeconds: null
}

const openAiProvider: WorkspaceSetupProviderSummary = {
  id: 'openai',
  displayName: 'OpenAI',
  protocol: 'openai-responses',
  hasApiKey: true,
  endPoint: 'https://api.openai.com/v1',
  networkTimeoutSeconds: null
}

function status(overrides: Partial<WorkspaceStatusPayload> = {}): WorkspaceStatusPayload {
  return {
    status: 'needs-setup',
    workspacePath: 'X:\\fixtures\\workspace',
    hasUserConfig: false,
    providers: [],
    ...overrides
  }
}

function renderWizard(
  workspaceStatus: WorkspaceStatusPayload,
  props: Partial<Parameters<typeof WorkspaceSetupWizard>[0]> = {}
) {
  return render(
    <LocaleProvider>
      <WorkspaceSetupWizard
        workspacePath="X:\\fixtures\\workspace"
        workspaceStatus={workspaceStatus}
        onChooseDifferentWorkspace={() => {}}
        onCancel={() => {}}
        {...props}
      />
    </LocaleProvider>
  )
}

function primary(name: string): HTMLElement {
  return screen.getByRole('button', { name })
}

async function continueStep(): Promise<void> {
  fireEvent.click(await screen.findByRole('button', { name: 'Continue' }))
}

async function create(): Promise<void> {
  const button = await screen.findByRole('button', { name: 'Create workspace' })
  await waitFor(() => expect(button).not.toBeDisabled())
  fireEvent.click(button)
}

function apiKeyInput(): HTMLElement {
  return screen.getByLabelText('API key', { selector: 'input' })
}

async function manualModelInput(): Promise<HTMLInputElement> {
  return waitFor(() => {
    const control = screen.getByLabelText('Model', { selector: 'input' })
    return control as HTMLInputElement
  })
}

describe('WorkspaceSetupWizard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue(undefined)
    runSetup.mockResolvedValue(undefined)
    listSetupModels.mockResolvedValue({ kind: 'success', models: [{ id: 'gpt-5.6' }] })
    loginSetupChatGpt.mockResolvedValue({ kind: 'success' })

    installDesktopApiMock({
      settings: { get: settingsGet, set: settingsSet },
      workspace: { listSetupModels, loginSetupChatGpt, runSetup }
    })
  })

  it('starts setup from the interstitial and disables it while opening', () => {
    const onStart = vi.fn()
    render(
      <LocaleProvider>
        <WorkspaceSetupInterstitial workspacePath="X:\\fixtures\\workspace" isOpening={false} onStart={onStart} onChooseDifferentWorkspace={() => {}} />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByRole('button', { name: /Start workspace setup/ }))
    expect(onStart).toHaveBeenCalledTimes(1)

    render(
      <LocaleProvider>
        <WorkspaceSetupInterstitial workspacePath="X:\\fixtures\\workspace" isOpening onStart={onStart} onChooseDifferentWorkspace={() => {}} />
      </LocaleProvider>
    )
    expect(screen.getAllByRole('button', { name: /Start workspace setup/ }).at(-1)).toBeDisabled()
  })

  it('lets the first section change folders and cancel the wizard', () => {
    const onChooseDifferentWorkspace = vi.fn()
    const onCancel = vi.fn()
    renderWizard(status(), { onChooseDifferentWorkspace, onCancel })

    fireEvent.click(screen.getByRole('button', { name: 'Change folder' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(onChooseDifferentWorkspace).toHaveBeenCalledTimes(1)
    expect(onCancel).toHaveBeenCalledTimes(1)
  })

  it('creates the workspace with a saved provider and keeps the workspace override', async () => {
    listSetupModels.mockResolvedValue({ kind: 'success', models: [{ id: 'claude-sonnet-4-5' }] })
    renderWizard(status({
      hasUserConfig: true,
      providers: [openAiProvider, anthropicProvider],
      userConfigDefaults: { providerId: 'anthropic', model: 'claude-opus-4-5' }
    }))

    await continueStep()
    expect(screen.getByRole('radio', { name: /^Anthropic/ })).toHaveAttribute('aria-checked', 'true')
    await continueStep()
    await create()

    await waitFor(() => expect(runSetup).toHaveBeenCalledOnce())
    expect(listSetupModels).toHaveBeenCalledWith({ providerId: 'anthropic' })
    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      providerMode: 'existing',
      providerId: 'anthropic',
      model: 'claude-sonnet-4-5',
      setAsUserDefault: false
    }))
  })

  it('submits another saved provider when it is chosen', async () => {
    renderWizard(status({ hasUserConfig: true, providers: [openAiProvider, anthropicProvider] }))

    await continueStep()
    fireEvent.click(screen.getByRole('radio', { name: /^OpenAI/ }))
    await continueStep()
    await create()

    await waitFor(() => expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      providerMode: 'existing',
      providerId: 'openai'
    })))
  })

  it('requires an API key before an OpenAI connection can continue and saves it as a new provider', async () => {
    renderWizard(status())

    await continueStep()
    fireEvent.click(screen.getByRole('radio', { name: /^OpenAI API key/ }))
    expect(primary('Continue')).toBeDisabled()
    fireEvent.change(apiKeyInput(), { target: { value: ' sk-test ' } })
    await continueStep()
    await create()

    await waitFor(() => expect(runSetup).toHaveBeenCalledOnce())
    expect(listSetupModels).toHaveBeenCalledWith({ provider: expect.objectContaining({ id: 'openai', apiKey: 'sk-test' }) })
    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      providerMode: 'create',
      model: 'gpt-5.6',
      setAsUserDefault: true,
      provider: expect.objectContaining({
        id: 'openai',
        protocol: 'openai-responses',
        authMethod: 'apiKey',
        apiKey: 'sk-test',
        endPoint: 'https://api.openai.com/v1'
      })
    }))
  })

  it('gives a new Anthropic connection a unique id next to a saved one', async () => {
    renderWizard(status({ hasUserConfig: true, providers: [anthropicProvider] }))

    await continueStep()
    fireEvent.click(screen.getByRole('button', { name: 'Connect something else' }))
    fireEvent.click(screen.getByRole('radio', { name: /^Anthropic API key/ }))
    fireEvent.change(apiKeyInput(), { target: { value: 'sk-ant-test' } })
    await continueStep()
    await create()

    await waitFor(() => expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      providerMode: 'create',
      provider: expect.objectContaining({ id: 'anthropic-2', protocol: 'anthropic', apiKey: 'sk-ant-test' })
    })))
  })

  it('creates another service with a derived id and the Responses format by default', async () => {
    listSetupModels.mockResolvedValue({ kind: 'unsupported' })
    renderWizard(status())

    await continueStep()
    fireEvent.click(screen.getByRole('radio', { name: /^Another service/ }))
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'My Router' } })
    fireEvent.change(screen.getByLabelText(/^Endpoint/), { target: { value: 'not a url' } })
    expect(primary('Continue')).toBeDisabled()
    fireEvent.change(screen.getByLabelText(/^Endpoint/), { target: { value: 'https://router.example/v1' } })
    await continueStep()
    fireEvent.change(await manualModelInput(), { target: { value: 'router-model' } })
    await create()

    await waitFor(() => expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      providerMode: 'create',
      model: 'router-model',
      provider: expect.objectContaining({
        id: 'my-router',
        displayName: 'My Router',
        protocol: 'openai-responses',
        endPoint: 'https://router.example/v1',
        apiKey: '',
        networkTimeoutSeconds: null
      })
    })))
  })

  it('requires a model before the workspace can be created', async () => {
    listSetupModels.mockResolvedValue({ kind: 'missing-key' })
    renderWizard(status({ hasUserConfig: true, providers: [openAiProvider] }))

    await continueStep()
    await continueStep()
    const input = await manualModelInput()
    expect(input).toHaveValue('')
    expect(primary('Create workspace')).toBeDisabled()

    fireEvent.change(input, { target: { value: 'gpt-4.1' } })
    await create()

    await waitFor(() => expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({ model: 'gpt-4.1' })))
  })

  it('falls back to manual model entry when the catalog request fails', async () => {
    listSetupModels.mockRejectedValue(new Error('backend failed'))
    renderWizard(status({ hasUserConfig: true, providers: [openAiProvider] }))

    await continueStep()
    await continueStep()
    fireEvent.change(await manualModelInput(), { target: { value: 'gpt-4.1' } })
    await create()

    await waitFor(() => expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({ model: 'gpt-4.1' })))
  })

  it('imports detected project instructions unless the user opts out', async () => {
    const withImport = status({
      hasUserConfig: true,
      providers: [openAiProvider],
      bootstrapImportSources: [{ id: 'claude', fileName: 'CLAUDE.md', path: 'X:\\fixtures\\CLAUDE.md', relativePath: '../CLAUDE.md' }]
    })
    const { unmount } = renderWizard(withImport)
    await continueStep()
    await continueStep()
    await continueStep()
    await create()
    await waitFor(() => expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({ bootstrapImportSourceId: 'claude' })))
    unmount()

    runSetup.mockClear()
    renderWizard(withImport)
    await continueStep()
    fireEvent.click(screen.getByRole('radio', { name: /^Start without instructions/ }))
    await continueStep()
    await continueStep()
    await create()
    await waitFor(() => expect(runSetup).toHaveBeenCalledOnce())
    expect(runSetup.mock.calls[0][0]).not.toHaveProperty('bootstrapImportSourceId')
  })

  it('hands the logo position and image to the completion handler', async () => {
    const onRunSetup = vi.fn().mockResolvedValue(undefined)
    renderWizard(status({ hasUserConfig: true, providers: [openAiProvider] }), { onRunSetup })

    await continueStep()
    await continueStep()
    await create()

    await waitFor(() => expect(onRunSetup).toHaveBeenCalledOnce())
    expect(onRunSetup.mock.calls[0][1]).toEqual({
      logoRect: expect.objectContaining({ width: expect.any(Number), height: expect.any(Number) }),
      logoSrc: expect.any(String)
    })
    expect(runSetup).not.toHaveBeenCalled()
  })

  it('shows a setup failure and lets the user try again', async () => {
    const onRunSetup = vi.fn()
      .mockRejectedValueOnce(new Error('config is read-only'))
      .mockResolvedValueOnce(undefined)
    renderWizard(status({ hasUserConfig: true, providers: [openAiProvider] }), { onRunSetup })

    await continueStep()
    await continueStep()
    await create()

    expect(await screen.findByRole('alert')).toHaveTextContent('config is read-only')
    await create()
    await waitFor(() => expect(onRunSetup).toHaveBeenCalledTimes(2))
  })
})
