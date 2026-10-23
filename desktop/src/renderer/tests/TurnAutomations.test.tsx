import { expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TurnAutomations } from '../components/conversation/TurnAutomations'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import type { ConversationItem, ConversationTurn } from '../types/conversation'

const definition = (id: string, name: string) => ({
  id, name, prompt: 'Review the build', status: 'active', executionMode: 'independent', notificationPolicy: 'important',
  schedule: { kind: 'every', everyMs: 300000, timeZone: 'UTC' }
})
const call = (id: string, result: unknown): ConversationItem => ({
  id, type: 'toolCall', status: 'completed', success: true, toolName: 'Automation', toolCallId: `${id}-call`,
  source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Automation' },
  presentation: { presentationId: 'core.automation' }, result: JSON.stringify(result)
} as ConversationItem)

it('shows the latest state of each automation the turn created or updated and opens it beside the conversation', () => {
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  useConversationStore.setState({
    turns: [{
      id: 'turn-1', status: 'completed', items: [
        call('create-a', { operation: 'create', automation: definition('a', 'Draft notes') }),
        call('create-b', { operation: 'create', automation: definition('b', 'Removed later') }),
        call('update-a', { operation: 'update', automation: definition('a', 'Update release notes') }),
        call('delete-b', { operation: 'delete', automation: definition('b', 'Removed later') }),
        call('list', { operation: 'list', automations: [definition('c', 'Listed only')] })
      ]
    } as ConversationTurn]
  })
  render(<LocaleProvider loadSettings={false}><TurnAutomations turnId="turn-1" /></LocaleProvider>)

  const card = screen.getByRole('button', { name: /Update release notes/ })
  expect(screen.queryByRole('button', { name: /Draft notes|Removed later|Listed only/ })).toBeNull()
  fireEvent.click(card)

  const tabs = useViewerTabStore.getState().getThreadState('thread-1').tabs.filter((tab) => tab.kind === 'automation')
  expect(tabs).toEqual([expect.objectContaining({ automationId: 'a', label: 'Update release notes' })])
  expect(useUIStore.getState().activeMainView).toBe('conversation')
  expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: tabs[0]!.id })
})
