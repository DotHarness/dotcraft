import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChatGptOAuthPanel } from '../components/settings/ChatGptOAuthPanel'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const onSignedIn = vi.fn()
const onAfterMutation = vi.fn()
function mount(selectedProviderUsable = true) {
  return render(<LocaleProvider><ChatGptOAuthPanel providerId="subscription" providerInfo={null}
    selectedProviderId="existing" selectedProviderUsable={selectedProviderUsable}
    onSignedIn={onSignedIn} onAfterMutation={onAfterMutation} /></LocaleProvider>)
}

describe('ChatGPT provider login completion', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    onAfterMutation.mockResolvedValue(true)
    sendRequest.mockResolvedValue({ providerId: 'subscription' })
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: { sendRequest, onNotification: () => () => {} } })
  })

  it('also opens the saved editor if workspace activation fails', async () => {
    sendRequest.mockImplementation(async (method) => {
      if (method === 'workspace/config/update') throw new Error('activation failed')
      return { providerId: 'subscription' }
    })
    mount(false)
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in with ChatGPT' }))
    await waitFor(() => expect(onAfterMutation).toHaveBeenCalledOnce())
    expect(onSignedIn).toHaveBeenCalledWith('subscription')
  })

  it('does not navigate or activate after leaving the editor', async () => {
    let finish!: (value: unknown) => void
    sendRequest.mockReturnValue(new Promise((resolve) => { finish = resolve }))
    const view = mount(false)
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in with ChatGPT' }))
    view.unmount()
    await act(async () => finish({ providerId: 'subscription' }))
    expect(onSignedIn).not.toHaveBeenCalled()
    expect(sendRequest).toHaveBeenCalledTimes(1)
  })

  it('retries refreshing a saved provider without repeating authentication', async () => {
    onAfterMutation.mockResolvedValueOnce(false).mockResolvedValueOnce(true)
    mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in with ChatGPT' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument())
    expect(sendRequest).toHaveBeenCalledTimes(1)
    expect(onSignedIn).toHaveBeenCalledOnce()
    expect(onAfterMutation).toHaveBeenCalledTimes(2)
  })

  it('keeps the editor retryable when login fails', async () => {
    sendRequest.mockRejectedValue(new Error('Authorization cancelled'))
    mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in with ChatGPT' }))
    expect(await screen.findByText('Authorization cancelled')).toBeInTheDocument()
    expect(onSignedIn).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Sign in with ChatGPT' })).not.toBeDisabled()
  })
})
