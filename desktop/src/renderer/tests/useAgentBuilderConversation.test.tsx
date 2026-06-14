import { useEffect } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { useAgentBuilderConversation } from '../components/agents/useAgentBuilderConversation'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'

const appServerSendRequest = vi.fn()

function Harness(): JSX.Element {
  const result = useAgentBuilderConversation({
    active: true,
    onResult: vi.fn()
  })

  useEffect(() => {
    void result.start({
      targetId: 'draft-agent',
      targetSource: 'workspace',
      initialDraftMarkdown: '---\nname: draft-agent\n---\n\nDraft body.\n',
      inputParts: [{ type: 'text', text: 'Build the agent from this intent.' }],
      config: { model: 'gpt-5.5' }
    }).catch(() => undefined)
  }, [result.start])

  return (
    <div>
      <span>{result.status}</span>
      {result.error && <span>{result.error}</span>}
    </div>
  )
}

function PassiveHarness(): JSX.Element {
  const result = useAgentBuilderConversation({
    active: true,
    onResult: vi.fn()
  })

  return <span>{result.status}</span>
}

describe('useAgentBuilderConversation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/start') {
        return { thread: { id: 'builder-thread' } }
      }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        appServer: {
          sendRequest: appServerSendRequest,
          onNotification: vi.fn(() => vi.fn())
        }
      }
    })

    useConversationStore.getState().reset()
    useConversationStore.setState({ workspacePath: 'F:\\dotcraft' })
    useThreadStore.getState().reset()
    useThreadStore.setState({ activeThreadId: 'previous-thread' })
  })

  it('does not create a builder thread until start is called', async () => {
    render(<PassiveHarness />)

    expect(screen.getByText('idle')).toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/start', expect.anything())
  })

  it('binds and syncs the draft before sending the initial builder prompt', async () => {
    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('ready')).toBeInTheDocument()
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', {
        threadId: 'builder-thread',
        input: [{ type: 'text', text: 'Build the agent from this intent.' }],
        identity: {
          channelName: 'dotcraft-desktop',
          userId: 'local',
          channelContext: 'workspace:F:\\dotcraft',
          workspacePath: 'F:\\dotcraft'
        }
      })
    })

    expect(appServerSendRequest.mock.calls.map(([method]) => method)).toEqual([
      'thread/start',
      'agent/profiles/builderDraft/update',
      'turn/start'
    ])
    expect(appServerSendRequest).toHaveBeenNthCalledWith(1, 'thread/start', {
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: 'workspace:F:\\dotcraft',
        workspacePath: 'F:\\dotcraft'
      },
      config: {
        model: 'gpt-5.5',
        agentBuilderTargetId: 'draft-agent',
        agentBuilderTargetSource: 'workspace'
      },
      historyMode: 'server'
    })
    expect(appServerSendRequest).toHaveBeenNthCalledWith(2, 'agent/profiles/builderDraft/update', {
      threadId: 'builder-thread',
      rawContent: '---\nname: draft-agent\n---\n\nDraft body.\n'
    })
    expect(appServerSendRequest).toHaveBeenNthCalledWith(3, 'turn/start', {
      threadId: 'builder-thread',
      input: [{ type: 'text', text: 'Build the agent from this intent.' }],
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: 'workspace:F:\\dotcraft',
        workspacePath: 'F:\\dotcraft'
      }
    })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/enqueue')).toBe(false)
    expect(useThreadStore.getState().activeThreadId).toBe('builder-thread')
  })

  it('surfaces initial turn/start failures instead of silently swallowing them', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/start') {
        return { thread: { id: 'builder-thread' } }
      }
      if (method === 'turn/start') {
        throw new Error('turn failed')
      }
      return {}
    })

    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('error')).toBeInTheDocument()
      expect(screen.getByText('turn failed')).toBeInTheDocument()
    })
    expect(useThreadStore.getState().activeThreadId).toBe('previous-thread')
  })
})
