import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ProviderAuthLoginState } from '@dotcraft/sdk/contracts'
import { LocaleProvider } from '../contexts/LocaleContext'
import { BackendAuthenticationPanel } from '../components/settings/backends/BackendAuthenticationPanel'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const authenticated = vi.fn()
let notify: (value: { method: string; params: unknown }) => void
const running = (id = 'one'): ProviderAuthLoginState => ({ loginId: id, providerId: 'test', revision: 0, status: 'running' })
const waiting = (id = 'one'): ProviderAuthLoginState => ({
  ...running(id), revision: 1, status: 'waiting', promptId: 'prompt',
  prompt: { kind: 'secret', message: 'Test credential' }
})

function mount() {
  return render(<LocaleProvider><BackendAuthenticationPanel providerId="test" method="oauth"
    methods={[{ id: 'oauth', displayName: 'Test', kind: 'oauth' }]} onAuthenticated={authenticated} /></LocaleProvider>)
}

describe('backend authentication interaction', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'provider/auth/status') return { isAuthenticated: false }
      if (method === 'provider/auth/login') return waiting()
      return {}
    })
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: { sendRequest, onNotification: (callback) => { notify = callback; return () => {} } }
    })
  })

  it('sends a secret only to the correlated prompt and accepts a newer completion', async () => {
    const view = mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    const field = await screen.findByLabelText('Test credential')
    expect(field).toHaveAttribute('type', 'password')
    fireEvent.change(field, { target: { value: 'test-secret' } })
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('provider/auth/input', {
      loginId: 'one', promptId: 'prompt', value: 'test-secret'
    }))
    await act(async () => notify({ method: 'provider/auth/updated', params: {
      ...running(), revision: 3, status: 'completed', authentication: { isAuthenticated: true }
    } }))
    await act(async () => notify({ method: 'provider/auth/updated', params: waiting() }))
    expect(authenticated).toHaveBeenCalledOnce()
    expect(screen.queryByLabelText('Test credential')).not.toBeInTheDocument()
    view.unmount()
  })

  it('cancels a pending login when the editor closes', async () => {
    const view = mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    await screen.findByLabelText('Test credential')
    view.unmount()
    expect(sendRequest).toHaveBeenCalledWith('provider/auth/cancel', { loginId: 'one' })
    expect(authenticated).not.toHaveBeenCalled()
  })

  it('cancels a login whose start response arrives after unmount', async () => {
    let complete!: (value: unknown) => void
    sendRequest.mockImplementation(async (method: string) => method === 'provider/auth/login'
      ? await new Promise((resolve) => { complete = resolve })
      : { isAuthenticated: false })
    const view = mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    view.unmount()
    await act(async () => complete(running()))
    expect(sendRequest).toHaveBeenCalledWith('provider/auth/cancel', { loginId: 'one' })
  })
})
