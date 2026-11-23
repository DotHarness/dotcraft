import './setupPluginRuntime'
import { render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { TurnThreadActions } from '../components/conversation/TurnThreadActions'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

it('resolves historical send cards on the target connection without mixing equal chat IDs', async () => {
  const projectId = 'remote:device:paired:project'
  const threadId = 'target-thread'
  const localProject = '/workspace/project'
  const date = '2026-01-01T00:00:00Z'
  const sendConnectionRequest = vi.fn().mockResolvedValue({ thread: { displayName: 'Remote target' } })
  installDesktopApiMock({ appServer: { sendConnectionRequest } })
  useThreadStore.getState().setThreadList([{
    id: threadId,
    displayName: 'Local target',
    workspacePath: localProject,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: date,
    lastActiveAt: date
  }], localProject)
  useWorkspaceProjectsStore.getState().setPayload({
    foregroundProjectId: localProject,
    foregroundWorkspacePath: localProject,
    secondaryLimit: 8,
    projects: [{
      projectId,
      connectionKey: projectId,
      connectionId: 'connection',
      kind: 'remote',
      path: localProject,
      name: 'Remote project',
      loaded: true,
      running: true,
      pinned: false,
      state: 'secondary',
      threads: [],
      threadCount: 0
    }]
  })

  const sendItem = (targetProject: string): ConversationItem => ({
    id: targetProject,
    type: 'dynamicToolCall',
    status: 'completed',
    createdAt: date,
    toolName: 'SendMessageToThread',
    pluginNamespace: 'desktop',
    success: true,
    structuredResult: { threadId, projectId: targetProject, accepted: true, queued: false }
  })
  const turns = [
    { id: 'turn-1', items: [sendItem(localProject), sendItem(projectId)] },
    { id: 'turn-2', items: [sendItem(projectId)] }
  ].map((turn): ConversationTurn => ({ ...turn, threadId: 'source-thread', status: 'completed', startedAt: date }))
  useConversationStore.setState({ turns })
  render(
    <LocaleProvider loadSettings={false}>
      {turns.map(({ id }) => <TurnThreadActions key={id} turnId={id} />)}
    </LocaleProvider>
  )

  expect(screen.getByRole('button', { name: 'Open chat Local target' })).toBeInTheDocument()
  expect(await screen.findAllByRole('button', { name: 'Open chat Remote target' })).toHaveLength(2)
  expect(sendConnectionRequest.mock.calls).toEqual([
    [projectId, 'thread/read', { threadId }, undefined, { connectionId: 'connection' }]
  ])
})
