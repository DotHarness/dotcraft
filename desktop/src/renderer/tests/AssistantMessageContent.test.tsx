import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { AssistantMessageContent } from '../components/conversation/AssistantMessageContent'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/conversation/MarkdownRenderer', () => ({
  MarkdownRenderer: ({ content }: { content: string }) => <div>{content}</div>
}))
vi.mock('../components/conversation/VisualizationFrame', () => ({ VisualizationFrame: () => <div /> }))
vi.mock('../components/conversation/generativeUi/GenerativeUiMessageBlock', () => ({ GenerativeUiMessageBlock: () => <div /> }))

const reference = 'visualize{"path":"../other/chart.html"}'

describe('AssistantMessageContent visualization repair', () => {
  const sendRequest = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
    useUIStore.setState({ composerPrefill: null })
    installDesktopApiMock({ initialLocale: 'en', appServer: { sendRequest } })
  })

  it('offers repair for an invalid reference without opening a view or sending a turn', () => {
    render(
      <LocaleProvider loadSettings={false}>
        <AssistantMessageContent text={reference} streaming={false} threadId="thread-test" turnId="turn-test" itemId="item-test" />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: 'Ask to fix' }))

    expect(useUIStore.getState().composerPrefill).toContain(reference)
    expect(sendRequest).not.toHaveBeenCalled()
  })
})
