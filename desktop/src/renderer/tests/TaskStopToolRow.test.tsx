import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { translate } from '../../shared/locales'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { ConversationItem } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'
import { makeSubAgent, makeSubAgentThread } from './subAgentFixtures'
import { coreToolFields } from './testToolPresentation'

function stop(overrides: Partial<ConversationItem> = {}): ConversationItem {
  return {
    id: 'stop', toolCallId: 'call-stop', type: 'toolCall', status: 'completed', toolName: 'RenamedStop',
    ...coreToolFields('TaskStop'),
    presentation: { presentationId: 'core.task', options: { operation: 'stop', kind: 'agent' } },
    arguments: { taskId: 'wrong_target' }, success: true, createdAt: '',
    result: JSON.stringify({ taskId: '/root/review_core', kind: 'agent', label: 'Core', status: 'stopped' }),
    ...overrides
  }
}

function renderStop(item: ConversationItem) {
  return render(
    <LocaleProvider loadSettings={false}>
      <ToolCallCard item={item} threadId="parent-B" turnId="turn-stop" turnRunning />
    </LocaleProvider>
  )
}

function agentButton(name: string) {
  return screen.getByRole('button', { name: translate('en', 'subagentsPanel.openAria', { name }) })
}

beforeEach(() => {
  installDesktopApiMock({ initialLocale: 'en' })
  useConnectionStore.getState().reset()
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  useThreadStore.setState({ activeThreadId: 'parent-B', threadList: [makeSubAgentThread('parent-B')] })
  useUIStore.setState({ activeDetailTab: { kind: 'launcher' } })
})

describe('agent TaskStop row', () => {
  it('opens the canonical child from its resolved name', () => {
    useSubAgentStore.getState().setChildren('parent-B', [makeSubAgent()])
    renderStop(stop())

    fireEvent.click(agentButton('Core B'))
    expect(useSubAgentStore.getState().selectedChildByParent.get('parent-B')).toBe('child-B')
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'subagents' })
  })

  it('resolves a running relative target from the containing thread path', () => {
    useThreadStore.setState({ threadList: [makeSubAgentThread('parent-B', {
      kind: 'subagent', subAgent: { rootThreadId: 'root-B', agentPath: '/root/reviewer' }
    })] })
    useSubAgentStore.getState().setChildren('parent-B', [makeSubAgent({ agentPath: '/root/reviewer/core' })])
    renderStop(stop({ status: 'started', arguments: { taskId: 'core' }, result: undefined, success: undefined }))

    fireEvent.click(agentButton('Core B'))
    expect(useSubAgentStore.getState().selectedChildByParent.get('parent-B')).toBe('child-B')
  })

  it('opens the list and clears the old selection when the child is unresolved', () => {
    useSubAgentStore.getState().selectChild('parent-B', 'old-selection')
    renderStop(stop())
    fireEvent.click(agentButton('Core'))
    expect(useSubAgentStore.getState().selectedChildByParent.has('parent-B')).toBe(false)
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'subagents' })
  })
})
