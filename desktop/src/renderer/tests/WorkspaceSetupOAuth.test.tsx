import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { WorkspaceSetupWizard } from '../components/WorkspaceSetupWizard'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'

const listSetupModels = vi.fn()
const loginSetupChatGpt = vi.fn()
const runSetup = vi.fn()
const provider = { id: 'subscription', displayName: 'Subscription', protocol: 'openai-responses',
  authMethod: 'chatgptOAuth', endPoint: '', hasApiKey: false, networkTimeoutSeconds: null } as const

function mount(existing: boolean) {
  render(<LocaleProvider><WorkspaceSetupWizard workspacePath="C:/test-workspace"
    workspaceStatus={{ status: 'needs-setup', workspacePath: 'C:/test-workspace', hasUserConfig: existing,
      providers: existing ? [provider] : [] }} onChooseDifferentWorkspace={() => {}} onCancel={() => {}} />
  </LocaleProvider>)
}

async function continueStep() {
  fireEvent.click(await screen.findByRole('button', { name: 'Continue' }))
}

async function create() {
  const button = await screen.findByRole('button', { name: 'Create workspace' })
  await waitFor(() => expect(button).not.toBeDisabled())
  fireEvent.click(button)
  await waitFor(() => expect(runSetup).toHaveBeenCalledOnce())
}

describe('Setup ChatGPT sign-in', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    listSetupModels.mockResolvedValue({ kind: 'success', models: [{ id: 'account-model' }] })
    loginSetupChatGpt.mockResolvedValue({ kind: 'success' })
    runSetup.mockResolvedValue(undefined)
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() },
      workspace: { listSetupModels, loginSetupChatGpt, runSetup } })
  })

  it('uses an authenticated saved provider without another sign-in', async () => {
    mount(true)
    await continueStep()
    await continueStep()
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
    await create()
    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({ providerMode: 'existing', providerId: 'subscription', model: 'account-model' }))
    expect(loginSetupChatGpt).not.toHaveBeenCalled()
  })

  it('signs a new ChatGPT connection in before the model step and saves it only on create', async () => {
    mount(false)
    await continueStep()
    expect(screen.queryByRole('button', { name: 'Continue' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(loginSetupChatGpt).toHaveBeenCalledWith('chatgpt'))
    await waitFor(() => expect(listSetupModels).toHaveBeenCalledWith({
      provider: expect.objectContaining({ id: 'chatgpt', authMethod: 'chatgptOAuth' })
    }))
    expect(runSetup).not.toHaveBeenCalled()
    await create()
    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({
      providerMode: 'create',
      model: 'account-model',
      setAsUserDefault: true,
      provider: expect.objectContaining({ id: 'chatgpt', protocol: 'openai-responses', authMethod: 'chatgptOAuth', apiKey: '' })
    }))
  })

  it('reports a failed sign-in and keeps sign-in available', async () => {
    loginSetupChatGpt.mockResolvedValue({ kind: 'error', errorMessage: 'Authorization cancelled' })
    mount(false)
    await continueStep()
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Authorization cancelled')
    expect(screen.getByRole('button', { name: 'Sign in' })).not.toBeDisabled()
    expect(listSetupModels).not.toHaveBeenCalled()
  })

  it('signs a saved provider in again when its catalog needs authorization', async () => {
    let authenticated = false
    listSetupModels.mockImplementation(async () => authenticated
      ? { kind: 'success', models: [{ id: 'account-model' }] }
      : { kind: 'auth-required' })
    loginSetupChatGpt.mockImplementation(async () => { authenticated = true; return { kind: 'success' } })
    mount(true)
    await continueStep()
    await continueStep()

    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(loginSetupChatGpt).toHaveBeenCalledWith('subscription'))
    await create()
    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({ providerMode: 'existing', providerId: 'subscription', model: 'account-model' }))
  })
})
