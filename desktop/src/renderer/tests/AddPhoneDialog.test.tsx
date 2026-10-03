// @vitest-environment jsdom
import { StrictMode } from 'react'
import { expect, it, vi } from 'vitest'
import { render, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AddPhoneDialog } from '../components/settings/panels/phones/AddPhoneDialog'
import { useMobileStore } from '../stores/mobileStore'
import { installDesktopApiMock } from './desktopApiMock'

it('mints one code per opening', async () => {
  const createPairing = vi.fn().mockResolvedValue({
    ok: true,
    value: {
      pairingId: 'pair_1',
      qrPayload: 'dotcraft://pair?v=1&code=abc',
      expiresAt: new Date(Date.now() + 10 * 60 * 1000).toISOString()
    }
  })
  installDesktopApiMock({
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    mobile: { createPairing }
  })

  render(
    <StrictMode>
      <LocaleProvider>
        <AddPhoneDialog onClose={() => undefined} />
      </LocaleProvider>
    </StrictMode>
  )

  await waitFor(() => expect(useMobileStore.getState().pairing.step).toBe('code'))
  expect(createPairing).toHaveBeenCalledTimes(1)
})
