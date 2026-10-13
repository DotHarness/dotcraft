import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import { useUIStore } from '../stores/uiStore'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

describe('memory update card', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useUIStore.setState({ activeMainView: 'conversation' })
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      appServer: { sendRequest: vi.fn(async () => ({})) }
    })
  })

  it('opens Personalization settings from Manage', () => {
    const item = withTestCorePresentation({
      id: 'memory-edit-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'EditFile',
      toolCallId: 'call-1',
      arguments: { path: '.craft/memory/user_role.md', oldText: 'a', newText: 'b' },
      result: 'ok',
      success: true,
      createdAt: '2026-10-13T00:00:00Z'
    }, CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'edit', memory: 'update' })

    render(<LocaleProvider><ToolCallCard threadId="thread-1" item={item} turnId="turn-1" /></LocaleProvider>)
    fireEvent.click(screen.getByRole('button', { expanded: false }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button'))

    expect(useUIStore.getState().activeMainView).toBe('settings')
    expect(useUIStore.getState().activeSettingsTab).toBe('personalization')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
