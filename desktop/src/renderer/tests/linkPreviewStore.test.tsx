import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react'
import { UserMessageBlock } from '../components/conversation/UserMessageBlock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useLinkPreview } from '../stores/linkPreviewStore'
import { installDesktopApiMock } from './desktopApiMock'

describe('useLinkPreview', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('opens a user message preview without requiring search source data', async () => {
    vi.useFakeTimers()
    const get = vi.fn().mockResolvedValue({ status: 'ready', title: 'Page title' })
    installDesktopApiMock({ linkPreview: { get } })
    useConversationStore.setState({ turns: [], workspacePath: '' })
    const url = 'https://example.com/user-preview'
    render(<LocaleProvider loadSettings={false}><UserMessageBlock text={url} /></LocaleProvider>)
    expect(get).not.toHaveBeenCalled()
    await act(async () => {
      fireEvent.mouseEnter(screen.getByRole('link'))
      vi.runOnlyPendingTimers()
    })
    expect(get).toHaveBeenCalledWith(url)
    expect(within(screen.getByRole('dialog')).getByRole('link')).toHaveAttribute('href', url)
    vi.useRealTimers()
  })

  it('looks up only once a card opens and recovers from a failure on a later opening', async () => {
    let now = 1_000_000
    vi.spyOn(Date, 'now').mockImplementation(() => now)
    const get = vi
      .fn()
      .mockResolvedValueOnce({ status: 'error', error: 'unavailable' })
      .mockResolvedValueOnce({ status: 'ready', title: 'Recovered' })
    installDesktopApiMock({ linkPreview: { get } })

    const url = 'https://example.com/recovers'
    const { result, rerender } = renderHook(({ openCount }) => useLinkPreview(url, openCount), {
      initialProps: { openCount: 0 }
    })
    expect(get).not.toHaveBeenCalled()

    rerender({ openCount: 1 })
    await waitFor(() => expect(result.current).toEqual({ status: 'error', error: 'unavailable' }))

    rerender({ openCount: 2 })
    expect(get).toHaveBeenCalledTimes(1)

    now += 31_000
    rerender({ openCount: 3 })
    await waitFor(() => expect(result.current).toEqual({ status: 'ready', title: 'Recovered' }))
    expect(get).toHaveBeenCalledTimes(2)
  })
})
