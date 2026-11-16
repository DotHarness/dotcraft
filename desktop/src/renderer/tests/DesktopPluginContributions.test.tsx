import './setupPluginRuntime'
import type { DesktopPluginCommandContext, DesktopPluginHost } from '@dotcraft/plugin'
import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { DesktopPluginToolDisclosure } from '../components/desktopPlugins/DesktopPluginToolDisclosure'
import { useToolCallRevealStore } from '../stores/toolCallRevealStore'
import { useState } from 'react'
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
import { planToolRunRender } from '../utils/toolCallAggregation'
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

describe('Desktop extension contribution registry', () => {
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

    expect(planToolRunRender([item('one'), item('two')], { isRunning: false, isTrailingRun: false }).map((entry) => entry.kind))
      .toEqual(['single', 'single'])
    withdrawDesktopPluginGeneration('fixture.plugin')
    expect(planToolRunRender([item('one'), item('two')], { isRunning: false, isTrailingRun: false }).map((entry) => entry.kind))
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

describe('Desktop extension contribution outlets', () => {
  it('projects changing progress snapshots and structured errors to an exact renderer', () => {
    const seen = vi.fn()
    const renderer = {
      ...activeToolRenderer('fixture.plugin', 'transfer', 'fixture.transfer', 10),
      component: ({ presentation }: import('@dotcraft/plugin').DesktopPluginToolRendererProps) => {
        seen(presentation)
        return null
      }
    }
    publishDesktopPluginGeneration(generation('fixture.plugin', { toolRenderers: [renderer] }))
    const item: ConversationItem = {
      id: 'transfer', type: 'toolCall', status: 'started', toolName: 'fixture.transfer',
      presentation: { presentationId: 'fixture.transfer' }, createdAt: '2026-11-16T00:00:00Z',
      transferProgress: {
        kind: 'remoteFileTransfer', stage: 'transferring', direction: 'upload',
        localPath: 'local', remotePath: 'remote', hostId: 'machine', hostDisplayName: 'Machine',
        transferredBytes: 10, completedBytes: 0, completedFiles: 0, totalBytes: 100
      }
    }
    const view = render(<LocaleProvider><ToolCallCard item={item} threadId="thread" turnId="turn" turnRunning /></LocaleProvider>)
    expect(seen.mock.lastCall?.[0].progress).toMatchObject({ transferredBytes: 10 })
    view.rerender(<LocaleProvider><ToolCallCard item={{
      ...item, status: 'completed', success: false, errorCode: 'transfer_failed',
      errorMessage: 'Transfer failed', transferProgress: { ...item.transferProgress!, transferredBytes: 25 }
    }} threadId="thread" turnId="turn" /></LocaleProvider>)
    expect(seen.mock.lastCall?.[0]).toMatchObject({
      status: 'completed', success: false, errorCode: 'transfer_failed', errorMessage: 'Transfer failed',
      progress: { transferredBytes: 25 }
    })
  })

  it('expands a plugin disclosure when its tool call is revealed', () => {
    function Disclosure(): JSX.Element {
      const [expanded, setExpanded] = useState(false)
      return <DesktopPluginToolDisclosure callId="fixture-call" expanded={expanded} onToggle={() => setExpanded(value => !value)} title="Fixture">
        <div data-testid="revealed-output">Output</div>
      </DesktopPluginToolDisclosure>
    }
    useToolCallRevealStore.setState({ request: null, target: null })
    render(<Disclosure />)
    expect(screen.queryByTestId('revealed-output')).toBeNull()
    act(() => useToolCallRevealStore.setState({
      target: { threadId: 'thread', turnId: 'turn', callId: 'fixture-call', nonce: 1 }
    }))
    expect(screen.getByRole('button', { name: 'Fixture' }).getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByTestId('revealed-output')).toBeInTheDocument()
    act(() => useToolCallRevealStore.setState({ request: null, target: null }))
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
          threadId="thread-1"
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
})
