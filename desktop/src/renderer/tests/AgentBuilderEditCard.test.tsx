import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import type { ConversationItem } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

function builderItem(
  field: string,
  toolName: string,
  args: Record<string, unknown>,
  result: Record<string, unknown> | null,
  running = false
): ConversationItem {
  return {
    id: `builder-${toolName}`,
    type: 'toolCall',
    status: running ? 'streaming' : 'completed',
    toolName,
    toolCallId: `call-${toolName}`,
    source: { kind: 'CoreNative', sourceId: 'agent-profile-builder' },
    presentation: { presentationId: 'core.agent-builder', options: { field } },
    arguments: running ? undefined : args,
    argumentsPreview: running ? '{"desc' : undefined,
    result: result ? JSON.stringify(result) : undefined,
    success: running ? undefined : result?.ok !== false,
    createdAt: new Date().toISOString()
  } as ConversationItem
}

function renderRow(item: ConversationItem, running = false): void {
  render(
    <LocaleProvider>
      <ToolCallCard threadId="thread-1" item={item} turnId="turn-1" turnRunning={running} />
    </LocaleProvider>
  )
}

describe('Agent Builder edit rows', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      appServer: { sendRequest: vi.fn(async () => ({})) }
    })
  })

  it('states a name edit with the value as an inline reference', () => {
    renderRow(builderItem('name', 'SetAgentName', { name: 'slate' }, {
      ok: true, field: 'name', change: { op: 'set', value: 'slate' }
    }))

    const row = screen.getByTestId('tool-row')
    expect(row).toHaveTextContent('Named the agent slate')
    expect(row.querySelector('.dc-ref-profile')).toHaveTextContent('slate')
    expect(row.getAttribute('data-expandable')).toBe('false')
  })

  it('shows the first tool names inline and the rest, with rejected names, behind the row', () => {
    renderRow(builderItem('tools.policy', 'SetAgentToolPolicy', {
      mode: 'allowList', names: ['ReadFile', 'GrepFiles', 'FindFiles', 'WebFetch', 'Imagegen']
    }, {
      ok: true,
      field: 'tools.policy',
      change: { op: 'set', mode: 'allowList', list: ['ReadFile', 'GrepFiles', 'FindFiles', 'WebFetch'], rejected: ['Imagegen'] }
    }))

    const row = screen.getByTestId('tool-row')
    expect(row).toHaveTextContent('Allowed only')
    expect(row).toHaveTextContent('+1 more')
    fireEvent.click(row)
    expect(screen.getByText('Skipped unknown: Imagegen')).toBeInTheDocument()
  })

  it('keeps appended instructions behind the row as rendered markdown', () => {
    renderRow(builderItem('instructions', 'AppendAgentInstructions', { text: '## Voice\n\nLead with the benefit.' }, {
      ok: true, field: 'instructions', change: { op: 'append', value: 'Body\n\n## Voice\n\nLead with the benefit.' }
    }))

    const row = screen.getByTestId('tool-row')
    expect(row).toHaveTextContent('Extended the instructions')
    fireEvent.click(row)
    expect(screen.getByRole('heading', { name: 'Voice' })).toBeInTheDocument()
  })

  it('reports a rejected edit in the error tone with its reason', () => {
    renderRow(builderItem('tools.policy', 'SetAgentToolPolicy', { mode: 'allowList', names: ['Bash'] }, {
      ok: false, field: 'tools.policy', error: 'Unknown tools: Bash'
    }))

    const row = screen.getByTestId('tool-row')
    expect(row).toHaveTextContent('Couldn’t update tools')
    expect(row).toHaveTextContent('Unknown tools: Bash')
    expect(row.closest('[data-tone="error"]')).not.toBeNull()
  })

  it('labels a running builder edit by the field it is updating', () => {
    renderRow(builderItem('description', 'SetAgentDescription', {}, null, true), true)

    expect(screen.getByTestId('tool-row')).toHaveTextContent('Updating description')
  })
})
