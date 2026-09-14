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

async function mount(existing = true) {
  render(<LocaleProvider><WorkspaceSetupWizard workspacePath="C:/test-workspace"
    workspaceStatus={{ status: 'needs-setup', workspacePath: 'C:/test-workspace', hasUserConfig: existing,
      providers: existing ? [provider] : [] }} onChooseDifferentWorkspace={() => {}} onCancel={() => {}} />
  </LocaleProvider>)
  fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
  await screen.findByLabelText('Model')
}
async function submit() {
  fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Create Workspace' }))
  await waitFor(() => expect(runSetup).toHaveBeenCalledOnce())
}

describe('Setup ChatGPT subscription', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    listSetupModels.mockResolvedValue({ kind: 'success', models: [{ id: 'account-model' }] })
    loginSetupChatGpt.mockResolvedValue({ kind: 'success' })
    runSetup.mockResolvedValue(undefined)
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() },
      workspace: { listSetupModels, loginSetupChatGpt, runSetup } })
  })

  it('uses an authenticated existing provider without another login and submits its model', async () => {
    await mount()
    await waitFor(() => expect(screen.getByLabelText('Model').tagName).toBe('BUTTON'))
    expect(screen.queryByRole('button', { name: 'Sign in with ChatGPT' })).not.toBeInTheDocument()
    await submit()
    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({ providerMode: 'existing', providerId: 'subscription', model: 'account-model' }))
    expect(loginSetupChatGpt).not.toHaveBeenCalled()
  })

  it('logs in a new draft, shows the model picker, and saves only on final submission', async () => {
    let authenticated = false
    listSetupModels.mockImplementation(async (request) => request.provider?.authMethod === 'chatgptOAuth'
      ? authenticated ? { kind: 'success', models: [{ id: 'account-model' }] } : { kind: 'auth-required' }
      : { kind: 'unsupported' })
    loginSetupChatGpt.mockImplementation(async () => { authenticated = true; return { kind: 'success' } })
    await mount(false)
    fireEvent.click(await screen.findByRole('button', { name: /Custom/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Sign in with ChatGPT/ }))
    const buttons = await waitFor(() => {
      const matches = screen.getAllByRole('button', { name: /Sign in with ChatGPT/ })
      expect(matches).toHaveLength(2)
      return matches
    })
    fireEvent.click(buttons.at(-1)!)
    await waitFor(() => expect(screen.getByLabelText('Model').tagName).toBe('BUTTON'))
    expect(runSetup).not.toHaveBeenCalled()
    await submit()
    expect(runSetup).toHaveBeenCalledWith(expect.objectContaining({ providerMode: 'create', model: 'account-model', provider: expect.objectContaining({ authMethod: 'chatgptOAuth' }) }))
  })

  it('keeps sign-in retry available and reports authorization failure separately', async () => {
    listSetupModels.mockResolvedValue({ kind: 'auth-required' })
    loginSetupChatGpt.mockResolvedValue({ kind: 'error', errorMessage: 'Authorization cancelled' })
    await mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in with ChatGPT' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Authorization cancelled')
    expect(screen.getByRole('button', { name: 'Sign in with ChatGPT' })).not.toBeDisabled()
  })
})
