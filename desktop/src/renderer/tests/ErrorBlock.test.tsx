import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ErrorBlock } from '../components/conversation/ErrorBlock'
import { installDesktopApiMock } from './desktopApiMock'

describe('ErrorBlock', () => {
  let writeTextMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    writeTextMock = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      }
    })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: writeTextMock
      }
    })
  })

  it('copies the error message from the card action', async () => {
    render(
      <LocaleProvider>
        <ErrorBlock message="Status Code: BadRequest" />
      </LocaleProvider>
    )

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Copy error' }))
      await Promise.resolve()
    })

    expect(writeTextMock).toHaveBeenCalledWith('Status Code: BadRequest')
    expect(screen.getByRole('button', { name: 'Error copied' })).toBeInTheDocument()
  })
})
