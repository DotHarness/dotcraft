import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ImportCompletedNotification, ImportOutcome } from '@dotcraft/sdk/contracts'
import { ImportHistory } from '../components/settings/panels/ImportHistory'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const chat = (sourceId: string, title: string): ImportOutcome => ({
  source: 'cursor', sourceId, category: 'sessions', scope: 'workspace', targetPath: '', status: 'imported', title, threadId: `thread_${sourceId}`
})
const batch = (importId: string, completedAt: string, outcomes: ImportOutcome[]): ImportCompletedNotification => ({
  importId, trigger: 'manual', startedAt: completedAt, completedAt, outcomes
})
const imports = [
  batch('latest', '2026-09-28T07:31:00Z', [chat('c1', 'Fix login')]),
  batch('older', '2026-09-20T07:31:00Z', [chat('c0', 'Older chat')])
]

function renderHistory(): void {
  render(<LocaleProvider loadSettings={false}><ImportHistory revision={0} /></LocaleProvider>)
}

describe('import history', () => {
  beforeEach(() => {
    installDesktopApiMock({ appServer: { sendRequest: vi.fn(async () => ({ imports, attention: [] })) } })
    useThreadStore.setState({ activeThreadId: null })
    useUIStore.setState({ activeMainView: 'settings' })
  })

  it('expands the latest import and reveals older ones on request', async () => {
    renderHistory()
    const latest = await screen.findAllByRole('button', { name: /Imported from Cursor/ })
    expect(latest).toHaveLength(1)
    expect(latest[0]).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'View more' }))

    const all = screen.getAllByRole('button', { name: /Imported from Cursor/ })
    expect(all).toHaveLength(2)
    expect(all[1]).toHaveAttribute('aria-expanded', 'false')
  })

  it('opens an imported chat from its category', async () => {
    renderHistory()
    fireEvent.click(await screen.findByRole('button', { name: /Chat sessions/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Open chat: Fix login' }))

    expect(useThreadStore.getState().activeThreadId).toBe('thread_c1')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })
})
