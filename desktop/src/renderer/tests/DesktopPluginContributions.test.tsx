import type { DesktopPluginCommandContext, DesktopPluginHost } from '@dotcraft/plugin'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CommandSearchPopover } from '../components/conversation/CommandSearchPopover'
import { AgentMessage } from '../components/conversation/AgentMessage'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import {
  DesktopPluginConversationTabs,
  DesktopPluginConversationViewOutlet
} from '../components/desktopPlugins/DesktopPluginConversationView'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  clearDesktopPluginRegistry,
  executeDesktopPluginCommand,
  findSelectedDesktopPluginConversationView,
  publishDesktopPluginGeneration,
  resolveDesktopPluginLabel,
  resolveDesktopPluginToolRenderer,
  selectDesktopPluginConversationView,
  useDesktopPluginRegistry,
  withdrawDesktopPluginGeneration,
  type ActiveDesktopPluginCommand,
  type ActiveDesktopPluginConversationView,
  type ActiveDesktopPluginToolRenderer,
  type DesktopPluginGeneration
} from '../plugins/desktopPluginRegistry'
import { useConversationStore } from '../stores/conversationStore'
import type { ConversationItem } from '../types/conversation'
import { aggregateToolCalls } from '../utils/toolCallAggregation'
import { installDesktopApiMock } from './desktopApiMock'

const revision = 'a'.repeat(64)

function host(pluginId: string): DesktopPluginHost {
  return {
    plugin: { id: pluginId, version: '1.0.0', displayName: `${pluginId} owner` }
  } as DesktopPluginHost
}

function generation(
  pluginId: string,
  values: Partial<Omit<DesktopPluginGeneration, 'pluginId' | 'version' | 'revision'>> = {}
): DesktopPluginGeneration {
  return {
    pluginId,
    version: '1.0.0',
    revision,
    mainViews: [],
    settingsPages: [],
    conversationViews: [],
    commands: [],
    toolRenderers: [],
    messageActions: [],
    ...values
  }
}

function activeCommand(
  pluginId: string,
  id: string,
  order: number,
  execute = vi.fn()
): ActiveDesktopPluginCommand {
  return {
    pluginId,
    revision,
    host: host(pluginId),
    contributionKey: `${pluginId}:${id}`,
    id,
    label: { default: id },
    order,
    execute
  }
}

function activeToolRenderer(
  pluginId: string,
  id: string,
  presentationId: string,
  priority: number
): ActiveDesktopPluginToolRenderer {
  return {
    pluginId,
    revision,
    host: host(pluginId),
    contributionKey: `${pluginId}:${id}`,
    id,
    presentationId,
    priority,
    component: ({ presentation }) => <div data-testid="plugin-tool">{presentation.presentationId}</div>
  }
}

function activeConversationView(pluginId: string, id: string): ActiveDesktopPluginConversationView {
  return {
    pluginId,
    revision,
    host: host(pluginId),
    contributionKey: `${pluginId}:${id}`,
    id,
    label: { default: 'Trajectory' },
    component: ({ threadId }) => <div data-testid="conversation-view">{threadId}</div>
  }
}

beforeEach(() => {
  clearDesktopPluginRegistry()
  useConversationStore.getState().reset()
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    appServer: { sendRequest: vi.fn(async () => ({})) }
  })
})

afterEach(() => act(() => clearDesktopPluginRegistry()))

describe('Desktop Plugin contribution registry', () => {
  it('uses deterministic visible and exact tool-renderer ordering across owners', () => {
    publishDesktopPluginGeneration(generation('z.plugin', {
      commands: [activeCommand('z.plugin', 'later', 20)],
      toolRenderers: [activeToolRenderer('z.plugin', 'renderer', 'core.read-file', 10)]
    }))
    publishDesktopPluginGeneration(generation('a.plugin', {
      commands: [activeCommand('a.plugin', 'first', 10)],
      toolRenderers: [
        activeToolRenderer('a.plugin', 'second', 'core.read-file', 10),
        activeToolRenderer('a.plugin', 'winner', 'core.read-file', 5)
      ]
    }))

    expect(useDesktopPluginRegistry.getState().commands.map((command) => command.id))
      .toEqual(['first', 'later'])
    expect(resolveDesktopPluginToolRenderer('core.read-file')?.id).toBe('winner')
    expect(resolveDesktopPluginToolRenderer('core.read')).toBeNull()

    withdrawDesktopPluginGeneration('a.plugin')
    expect(resolveDesktopPluginToolRenderer('core.read-file')?.pluginId).toBe('z.plugin')
  })

  it('resolves labels by app locale whichever tag the plugin used as a key', () => {
    const label = { default: 'Board', translations: { 'zh-CN': '看板', 'pt-BR': 'Quadro' } }

    expect(resolveDesktopPluginLabel(label, 'zh-Hans')).toBe('看板')
    expect(resolveDesktopPluginLabel(label, 'zh-CN')).toBe('看板')
    expect(resolveDesktopPluginLabel(label, 'en')).toBe('Board')
    expect(resolveDesktopPluginLabel({ default: 'Board', translations: { ja: '掲示板' } }, 'ko'))
      .toBe('Board')
  })

  it('keeps exact plugin-rendered tools out of Core aggregation', () => {
    const renderer = activeToolRenderer('fixture.plugin', 'read', 'core.read-file', 10)
    const item = (id: string): ConversationItem => ({
      id,
      type: 'toolCall',
      status: 'completed',
      toolName: 'ReadFile',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
      presentation: { presentationId: 'core.read-file' },
      result: 'content',
      success: true,
      createdAt: '2026-08-27T00:00:00.000Z'
    })
    publishDesktopPluginGeneration(generation('fixture.plugin', { toolRenderers: [renderer] }))

    expect(aggregateToolCalls([item('one'), item('two')]).map((entry) => entry.kind))
      .toEqual(['single', 'single'])
    withdrawDesktopPluginGeneration('fixture.plugin')
    expect(aggregateToolCalls([item('one'), item('two')]).map((entry) => entry.kind))
      .toEqual(['group'])
  })

  it('keeps conversation selection per thread and removes stale selections with the generation', () => {
    const view = activeConversationView('fixture.plugin', 'trajectory')
    publishDesktopPluginGeneration(generation('fixture.plugin', { conversationViews: [view] }))

    selectDesktopPluginConversationView('thread-a', view.contributionKey)
    expect(findSelectedDesktopPluginConversationView('thread-a')?.id).toBe('trajectory')
    expect(findSelectedDesktopPluginConversationView('thread-b')).toBeNull()

    withdrawDesktopPluginGeneration('fixture.plugin')
    expect(findSelectedDesktopPluginConversationView('thread-a')).toBeNull()
    selectDesktopPluginConversationView('thread-a', view.contributionKey)
    expect(findSelectedDesktopPluginConversationView('thread-a')).toBeNull()
  })

  it('executes an available local command once and no longer resolves it after withdrawal', () => {
    const execute = vi.fn()
    const command = activeCommand('fixture.plugin', 'inspect-ui', 10, execute)
    const context: DesktopPluginCommandContext = {
      workspacePath: null,
      threadId: 'thread-a',
      viewId: 'conversation'
    }
    publishDesktopPluginGeneration(generation('fixture.plugin', { commands: [command] }))

    executeDesktopPluginCommand(command.contributionKey, context)
    expect(execute).toHaveBeenCalledOnce()
    expect(execute).toHaveBeenCalledWith(context, command.host)

    withdrawDesktopPluginGeneration('fixture.plugin')
    executeDesktopPluginCommand(command.contributionKey, context)
    expect(execute).toHaveBeenCalledOnce()
  })
})

describe('Desktop Plugin contribution outlets', () => {
  it('keeps Chat in the conversation tab strip and renders the selected plugin body', () => {
    const view = activeConversationView('fixture.plugin', 'trajectory')
    publishDesktopPluginGeneration(generation('fixture.plugin', { conversationViews: [view] }))
    const { rerender } = render(
      <LocaleProvider><DesktopPluginConversationTabs threadId="thread-a" /></LocaleProvider>
    )

    expect(screen.getByRole('tab', { name: 'Chat' })).toHaveAttribute('aria-selected', 'true')
    fireEvent.click(screen.getByRole('tab', { name: 'Trajectory' }))
    expect(screen.getByRole('tab', { name: 'Trajectory' })).toHaveAttribute('aria-selected', 'true')

    rerender(
      <LocaleProvider><DesktopPluginConversationViewOutlet contribution={view} threadId="thread-a" /></LocaleProvider>
    )
    expect(screen.getByTestId('conversation-view')).toHaveTextContent('thread-a')
  })

  it('uses an exact plugin renderer before an existing Core renderer and restores Core after withdrawal', () => {
    const renderer = activeToolRenderer('fixture.plugin', 'read', 'core.read-file', 10)
    publishDesktopPluginGeneration(generation('fixture.plugin', { toolRenderers: [renderer] }))
    const item: ConversationItem = {
      id: 'read-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'ReadFile',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
      presentation: { presentationId: 'core.read-file' },
      arguments: { path: 'README.md' },
      result: 'content',
      success: true,
      createdAt: '2026-08-27T00:00:00.000Z'
    }
    render(
      <LocaleProvider><ToolCallCard item={item} turnId="turn-a" /></LocaleProvider>
    )
    expect(screen.getByTestId('plugin-tool')).toHaveTextContent('core.read-file')

    act(() => withdrawDesktopPluginGeneration('fixture.plugin'))
    expect(screen.queryByTestId('plugin-tool')).toBeNull()
    expect(screen.getByRole('button')).toBeInTheDocument()
  })

  it('isolates a failed plugin renderer and renders the Core fallback', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    const renderer = {
      ...activeToolRenderer('fixture.plugin', 'read', 'core.read-file', 10),
      component: () => {
        throw new Error('renderer failed')
      }
    }
    publishDesktopPluginGeneration(generation('fixture.plugin', { toolRenderers: [renderer] }))

    render(
      <LocaleProvider>
        <ToolCallCard
          item={{
            id: 'read-failed',
            type: 'toolCall',
            status: 'completed',
            toolName: 'ReadFile',
            source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
            presentation: { presentationId: 'core.read-file' },
            arguments: { path: 'README.md' },
            result: 'content',
            success: true,
            createdAt: '2026-08-27T00:00:00.000Z'
          }}
          turnId="turn-a"
        />
      </LocaleProvider>
    )

    expect(screen.getByRole('button')).toBeInTheDocument()
    expect(error).toHaveBeenCalled()
    error.mockRestore()
  })

  it('shows Desktop commands beside AppServer commands without invoking the AppServer selection path', () => {
    const command = activeCommand('fixture.plugin', 'Open inspector', 10)
    const selectDesktop = vi.fn()
    const selectAppServer = vi.fn()
    render(
      <LocaleProvider>
        <CommandSearchPopover
          query=""
          visible
          loading={false}
          commands={[{
            name: '/review',
            aliases: [],
            description: 'Review files',
            category: 'review',
            requiresAdmin: false
          }]}
          desktopCommands={[command]}
          onSelectCommand={selectAppServer}
          onSelectDesktopCommand={selectDesktop}
          onDismiss={() => {}}
        />
      </LocaleProvider>
    )

    expect(screen.getByText('Desktop plugins')).toBeInTheDocument()
    expect(screen.getByText('Commands')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('option', { name: /Open inspector/i }))
    expect(selectDesktop).toHaveBeenCalledOnce()
    expect(selectDesktop).toHaveBeenCalledWith(command.contributionKey)
    expect(selectAppServer).not.toHaveBeenCalled()
  })

  it('renders an accessible assistant-message action and executes it once', () => {
    const execute = vi.fn()
    const pluginHost = host('fixture.plugin')
    publishDesktopPluginGeneration(generation('fixture.plugin', {
      messageActions: [{
        pluginId: 'fixture.plugin', revision, host: pluginHost, contributionKey: 'fixture.plugin:save',
        id: 'save', label: { default: 'Save insight' }, execute
      }]
    }))
    const message = {
      id: 'message-a',
      threadId: 'thread-a',
      turnId: 'turn-a',
      text: 'A useful answer.'
    }
    render(
      <LocaleProvider>
        <AgentMessage
          text={message.text}
          threadId={message.threadId}
          turnId={message.turnId}
          itemId={message.id}
        />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: 'Save insight' }))
    expect(execute).toHaveBeenCalledOnce()
    expect(execute).toHaveBeenCalledWith(message, pluginHost)
  })
})
