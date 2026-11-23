import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { ProviderSignIn } from '../components/settings/providers/ProviderSignIn'
import { LocaleProvider } from '../contexts/LocaleContext'

function mockAppServer(sendRequest: ReturnType<typeof vi.fn>): void {
  Object.defineProperty(window, 'api', {
    value: {
      appServer: {
        sendRequest,
        onNotification: vi.fn(() => () => undefined)
      },
      shell: {
        openExternal: vi.fn(() => Promise.resolve())
      }
    },
    configurable: true
  })
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

it('disables the action button while initial status is unknown and enables sign-out once authenticated', async () => {
  let resolveStatus!: (value: unknown) => void
  const sendRequest = vi.fn((method: string) => {
    if (method === 'provider/auth/status') return new Promise((resolve) => { resolveStatus = resolve })
    return Promise.resolve({})
  })
  mockAppServer(sendRequest)

  render(
    <LocaleProvider loadSettings={false}>
      <ProviderSignIn
        providerId="sample-provider"
        method="cli"
        title="Sample Provider"
        hint="Connect account"
      />
    </LocaleProvider>
  )

  expect(screen.getByRole('button').hasAttribute('disabled')).toBe(true)

  await act(async () => {
    resolveStatus({ isAuthenticated: true, displayName: 'CLI Account' })
  })

  await waitFor(() => {
    expect(screen.getByRole('button').hasAttribute('disabled')).toBe(false)
  })

  fireEvent.click(screen.getByRole('button'))
  await waitFor(() => {
    expect(sendRequest).toHaveBeenCalledWith('provider/auth/logout', { providerId: 'sample-provider' }, 30_000)
  })
})

it('keeps the action wired to sign-out when initialAuthenticated is true before status resolves', async () => {
  const sendRequest = vi.fn((method: string) => {
    if (method === 'provider/auth/status') return new Promise(() => undefined)
    return Promise.resolve({})
  })
  mockAppServer(sendRequest)

  render(
    <LocaleProvider loadSettings={false}>
      <ProviderSignIn
        providerId="sample-provider"
        initialAuthenticated={true}
        method="cli"
        title="Sample Provider"
        hint="Connect account"
      />
    </LocaleProvider>
  )

  const button = screen.getByRole('button')
  expect(button.hasAttribute('disabled')).toBe(false)

  fireEvent.click(button)
  await waitFor(() => {
    expect(sendRequest).toHaveBeenCalledWith('provider/auth/logout', { providerId: 'sample-provider' }, 30_000)
  })
})
