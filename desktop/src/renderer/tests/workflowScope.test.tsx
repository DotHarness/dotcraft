import { act, render, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { WorkflowToolCard } from '../components/workflow/WorkflowToolCard'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { selectWorkflowRunEntry, useWorkflowRunStore } from '../stores/workflowRunStore'

describe('mounted Workflow connection scope', () => {
  it('retains known runs through disconnect and reloads the same ids on a new connection', async () => {
    const request = vi.fn(async () => ({
      run: {
        runId: 'run-1', threadId: 'thread-1',
        name: useConnectionStore.getState().connectionKey,
        status: 'running', phases: [], createdAt: '2026-11-09T00:00:00Z'
      }
    }))
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: { appServer: {
        sendRequestRaw: request,
        onNotificationRaw: () => () => undefined
      } }
    })
    useWorkspaceProjectsStore.setState({ foregroundProjectId: 'project-1' })
    useConnectionStore.setState({ connectionKey: 'server-a', status: 'connected' })
    const entry = () => selectWorkflowRunEntry(useWorkflowRunStore.getState().entries, 'thread-1', 'run-1')
    const view = render(<LocaleProvider loadSettings={false}><WorkflowToolCard threadId="thread-1" runId="run-1" /></LocaleProvider>)
    await waitFor(() => expect(entry()?.run?.name).toBe('server-a'))

    act(() => {
      useConnectionStore.setState((state) => ({
        connectionEpoch: state.connectionEpoch + 1, connectionKey: null, status: 'disconnected'
      }))
    })
    expect(entry()?.run?.name).toBe('server-a')
    act(() => {
      useConnectionStore.setState((state) => ({
        connectionEpoch: state.connectionEpoch + 1, connectionKey: 'server-a', status: 'connected'
      }))
    })
    await waitFor(() => expect(request).toHaveBeenCalledTimes(2))

    act(() => {
      useConnectionStore.setState((state) => ({
        connectionEpoch: state.connectionEpoch + 1, connectionKey: 'server-b', status: 'connected'
      }))
    })
    await waitFor(() => expect(entry()?.run?.name).toBe('server-b'))
    expect(request).toHaveBeenLastCalledWith('workflow/run/read', { threadId: 'thread-1', runId: 'run-1' })
    view.unmount()
  })
})
