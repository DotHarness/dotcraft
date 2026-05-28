import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SystemNoticeBlock } from '../components/conversation/SystemNoticeBlock'
import type { ConversationItem } from '../types/conversation'
import type { AppLocale } from '../../shared/locales'

function compactedNotice(
  trigger: 'auto' | 'manual' | 'reactive' = 'manual',
  mode: 'partial' | 'micro' = 'partial'
): ConversationItem {
  return {
    id: 'notice-1',
    type: 'systemNotice',
    status: 'completed',
    createdAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    systemNotice: {
      kind: 'compacted',
      trigger,
      mode,
      tokensBefore: 180_000,
      tokensAfter: 44_000,
      percentLeftAfter: 0.78
    }
  }
}

function renderWithLocale(locale: AppLocale, item: ConversationItem = compactedNotice()): void {
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: {
        get: vi.fn().mockResolvedValue({ locale })
      }
    }
  })

  render(
    <LocaleProvider>
      <SystemNoticeBlock item={item} />
    </LocaleProvider>
  )
}

describe('SystemNoticeBlock', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders manual compacted notices with English copy and no token detail', () => {
    renderWithLocale('en')

    expect(screen.getByText('Context compacted')).toBeInTheDocument()
    expect(screen.queryByText(/Freed/i)).toBeNull()
    expect(screen.queryByText(/remaining/i)).toBeNull()
    expect(screen.queryByText(/44\.0k|136\.0k|78%/i)).toBeNull()
  })

  it('renders auto compacted notices with English copy', () => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: vi.fn().mockResolvedValue({ locale: 'en' })
        }
      }
    })

    render(
      <LocaleProvider>
        <SystemNoticeBlock item={compactedNotice('auto')} />
      </LocaleProvider>
    )

    expect(screen.getByText('Context automatically compacted')).toBeInTheDocument()
    expect(screen.queryByText(/Freed|remaining|78%/i)).toBeNull()
  })

  it('hides legacy micro compacted notices', () => {
    renderWithLocale('en', compactedNotice('auto', 'micro'))

    expect(screen.queryByText(/Context/i)).toBeNull()
  })

  it('renders manual compacted notices with Chinese copy', async () => {
    renderWithLocale('zh-Hans')

    await waitFor(() => {
      expect(screen.getByText('上下文已压缩')).toBeInTheDocument()
    })
    expect(screen.queryByText(/释放|剩余|tokens|78%/i)).toBeNull()
  })
})
