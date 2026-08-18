import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SystemNoticeBlock } from '../components/conversation/SystemNoticeBlock'
import type { ConversationItem } from '../types/conversation'
import type { AppLocale } from '../../shared/locales'
import { installDesktopApiMock } from './desktopApiMock'

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

function forkedNotice(): ConversationItem {
  return {
    id: 'notice-forked',
    type: 'systemNotice',
    status: 'completed',
    createdAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    systemNotice: {
      kind: 'forked',
      sourceThreadId: 'thread-source'
    }
  }
}

function renderWithLocale(locale: AppLocale, item: ConversationItem = compactedNotice()): void {
  installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale }) } })

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
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) } })

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

  it('renders forked notices with English copy', () => {
    renderWithLocale('en', forkedNotice())

    expect(screen.getByText('Forked from conversation')).toBeInTheDocument()
    expect(
      screen.getByRole('separator', { name: 'Forked from conversation' })
    ).toBeInTheDocument()
  })

  it('renders manual compacted notices with Chinese copy', async () => {
    renderWithLocale('zh-Hans')

    await waitFor(() => {
      expect(screen.getByText('上下文已压缩')).toBeInTheDocument()
    })
    expect(screen.queryByText(/释放|剩余|tokens|78%/i)).toBeNull()
  })

  it('renders forked notices with Chinese copy', async () => {
    renderWithLocale('zh-Hans', forkedNotice())

    await waitFor(() => {
      expect(screen.getByText('从会话 Fork')).toBeInTheDocument()
    })
    expect(screen.getByRole('separator', { name: '从会话 Fork' })).toBeInTheDocument()
  })
})
