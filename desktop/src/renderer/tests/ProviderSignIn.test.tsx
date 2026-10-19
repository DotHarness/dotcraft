import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ProviderAuthLoginState } from '@dotcraft/sdk/contracts'
import { ProviderSignIn } from '../components/settings/providers/ProviderSignIn'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'

type Notify = (payload: { method: string; params: unknown }) => void

const sendRequest = vi.fn()
const openExternal = vi.fn()
let notify: Notify | null = null

function loginState(revision: number, overrides: Partial<ProviderAuthLoginState> = {}): ProviderAuthLoginState {
  return { loginId: 'login-1', providerId: 'chatgpt', revision, status: 'running', ...overrides }
}

function push(state: ProviderAuthLoginState): void {
  act(() => notify?.({ method: 'provider/auth/updated', params: state }))
}

function requests(method: string): unknown[] {
  return sendRequest.mock.calls.filter(([name]) => name === method).map(([, params]) => params)
}

function renderSignIn(props: Partial<Parameters<typeof ProviderSignIn>[0]> = {}) {
  return render(
    <LocaleProvider>
      <ProviderSignIn providerId="chatgpt" method="chatgptOAuth" title="ChatGPT subscription" hint="" {...props} />
    </LocaleProvider>
  )
}

describe('ProviderSignIn', () => {
  let loginResponse: () => Promise<ProviderAuthLoginState>
  let stateResponse: ProviderAuthLoginState

  beforeEach(() => {
    vi.resetAllMocks()
    notify = null
    loginResponse = async () => loginState(1)
    stateResponse = loginState(1)
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'provider/auth/status') return { isAuthenticated: false }
      if (method === 'provider/auth/login') return loginResponse()
      if (method === 'provider/auth/state') return stateResponse
      return {}
    })
    openExternal.mockResolvedValue(undefined)
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      shell: { openExternal },
      appServer: {
        sendRequest,
        onNotification: (callback: Notify) => {
          notify = callback
          return () => { notify = null }
        }
      }
    })
  })

  it('applies an update that arrives before the login response and recovers state once', async () => {
    loginResponse = async () => {
      push(loginState(2, { event: { kind: 'auth_url', url: 'https://auth.example.test/start' } }))
      return loginState(1)
    }
    stateResponse = loginState(2, { event: { kind: 'auth_url', url: 'https://auth.example.test/start' } })
    renderSignIn()

    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(openExternal).toHaveBeenCalledWith('https://auth.example.test/start'))
    await waitFor(() => expect(requests('provider/auth/state')).toEqual([{ loginId: 'login-1' }]))
    expect(requests('provider/auth/login')).toEqual([{ providerId: 'chatgpt', method: 'chatgptOAuth' }])
    expect(openExternal).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeEnabled()
  })

  it('merges only newer revisions and reports completion once', async () => {
    const onSignedIn = vi.fn()
    renderSignIn({ onSignedIn })
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    await waitFor(() => expect(requests('provider/auth/state')).toHaveLength(1))

    push(loginState(3, { status: 'completed', authentication: { isAuthenticated: true, email: 'me@example.test' } }))
    push(loginState(2, { status: 'failed', errorMessage: 'stale failure' }))
    push(loginState(3, { status: 'completed', authentication: { isAuthenticated: true } }))

    expect(await screen.findByRole('button', { name: 'Sign out' })).toBeInTheDocument()
    expect(screen.queryByText(/stale failure/)).not.toBeInTheDocument()
    expect(onSignedIn).toHaveBeenCalledExactlyOnceWith('chatgpt')
  })

  it('answers a secret prompt for the current login and prompt', async () => {
    renderSignIn({ method: 'device' })
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    await waitFor(() => expect(requests('provider/auth/state')).toHaveLength(1))

    push(loginState(2, { status: 'waiting', promptId: 'prompt-1', prompt: { kind: 'secret', message: 'Access key' } }))
    const field = await screen.findByLabelText('Access key')
    expect(field).toHaveAttribute('type', 'password')
    fireEvent.change(field, { target: { value: 'secret-value' } })
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))

    await waitFor(() => expect(requests('provider/auth/input')).toEqual([
      { loginId: 'login-1', promptId: 'prompt-1', value: 'secret-value' }
    ]))
    await waitFor(() => expect(field).toHaveValue(''))
  })

  it('cancels the running login on request and when it unmounts', async () => {
    const view = renderSignIn()
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    await waitFor(() => expect(requests('provider/auth/state')).toHaveLength(1))

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(requests('provider/auth/cancel')).toEqual([{ loginId: 'login-1' }]))

    view.unmount()
    expect(requests('provider/auth/cancel')).toEqual([{ loginId: 'login-1' }, { loginId: 'login-1' }])
  })

  it('saves the provider before starting the login', async () => {
    const prepare = vi.fn(async () => 'saved-id')
    renderSignIn({ providerId: null, prepare })

    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(requests('provider/auth/login')).toEqual([{ providerId: 'saved-id', method: 'chatgptOAuth' }]))
    expect(prepare).toHaveBeenCalledOnce()
  })

  it('ignores an authenticated status that arrives after unmount', async () => {
    let resolveStatus: (value: unknown) => void = () => {}
    const onSignedIn = vi.fn()
    sendRequest.mockImplementation(async (method: string, params: { providerId?: string }) => {
      if (method === 'provider/auth/status' && params.providerId === 'saved-id') {
        return new Promise((resolve) => { resolveStatus = resolve })
      }
      if (method === 'provider/auth/status') return { isAuthenticated: false }
      return {}
    })
    const { unmount } = renderSignIn({ providerId: null, prepare: async () => 'saved-id', onSignedIn })

    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    await waitFor(() => expect(requests('provider/auth/status')).toContainEqual({ providerId: 'saved-id' }))
    unmount()
    await act(async () => resolveStatus({ isAuthenticated: true }))

    expect(onSignedIn).not.toHaveBeenCalled()
    expect(requests('provider/auth/login')).toEqual([])
  })
})
