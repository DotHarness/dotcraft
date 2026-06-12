import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAutomationsStore, type AutomationTask } from '../stores/automationsStore'
import { useCronStore } from '../stores/cronStore'

describe('automationsStore templates', () => {
  const sendRequest = vi.fn()

  beforeEach(() => {
    sendRequest.mockReset()
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {}
    })
    Object.defineProperty(globalThis.window, 'api', {
      configurable: true,
      value: {
        appServer: {
          sendRequest
        }
      }
    })

    useAutomationsStore.setState({
      tasks: [],
      loading: false,
      error: null,
      selectedTaskId: null,
      templates: [],
      templatesLoaded: false,
      templatesLocale: undefined
    })
    useCronStore.setState({
      jobs: [],
      loading: false,
      listLoadedOnce: false,
      error: null,
      selectedCronJobId: null
    })
  })

  it('passes the requested locale when fetching templates', async () => {
    sendRequest.mockResolvedValueOnce({
      templates: [
        {
          id: 'scan-commits-for-bugs',
          title: '扫描近期提交中的潜在缺陷',
          workflowMarkdown: '---\n---'
        }
      ]
    })

    await useAutomationsStore.getState().fetchTemplates('zh-Hans')

    expect(sendRequest).toHaveBeenCalledWith('automation/template/list', {
      locale: 'zh-Hans'
    })
    expect(useAutomationsStore.getState().templatesLocale).toBe('zh-Hans')
    expect(useAutomationsStore.getState().templates[0]?.title).toBe(
      '扫描近期提交中的潜在缺陷'
    )
  })

  it('refetches when locale changes but reuses the same-locale cache', async () => {
    sendRequest
      .mockResolvedValueOnce({
        templates: [{ id: 'weekly-report', title: 'Weekly activity report', workflowMarkdown: '' }]
      })
      .mockResolvedValueOnce({
        templates: [{ id: 'weekly-report', title: '每周活动报告', workflowMarkdown: '' }]
      })

    await useAutomationsStore.getState().fetchTemplates('en')
    await useAutomationsStore.getState().fetchTemplates('en')
    await useAutomationsStore.getState().fetchTemplates('zh-Hans')

    expect(sendRequest).toHaveBeenCalledTimes(2)
    expect(sendRequest).toHaveBeenNthCalledWith(1, 'automation/template/list', {
      locale: 'en'
    })
    expect(sendRequest).toHaveBeenNthCalledWith(2, 'automation/template/list', {
      locale: 'zh-Hans'
    })
    expect(useAutomationsStore.getState().templates[0]?.title).toBe('每周活动报告')
  })

  it('does not send task-level review fields when creating tasks', async () => {
    sendRequest.mockResolvedValueOnce({}).mockResolvedValueOnce({ tasks: [] })

    await useAutomationsStore.getState().createTask({
      title: 'Ship cleanup',
      description: 'Remove stale review gates',
      approvalPolicy: 'workspaceScope',
      workspaceMode: 'project'
    })

    expect(sendRequest).toHaveBeenNthCalledWith(1, 'automation/task/create', {
      title: 'Ship cleanup',
      description: 'Remove stale review gates',
      approvalPolicy: 'workspaceScope',
      workspaceMode: 'project'
    })
    expect(sendRequest.mock.calls[0][1]).not.toHaveProperty('requireApproval')
  })

  it('sends canonical worktree mode when creating tasks', async () => {
    sendRequest.mockResolvedValueOnce({}).mockResolvedValueOnce({ tasks: [] })

    await useAutomationsStore.getState().createTask({
      title: 'Build game',
      description: 'Create a mini game',
      workspaceMode: 'worktree'
    })

    expect(sendRequest).toHaveBeenNthCalledWith(1, 'automation/task/create', {
      title: 'Build game',
      description: 'Create a mini game',
      approvalPolicy: 'workspaceScope',
      workspaceMode: 'worktree'
    })
  })

  it('fetches managed worktree status for a worktree task', async () => {
    const status = {
      threadId: 'thread-1',
      worktree: {
        id: 'wt-1',
        sourceThreadId: 'thread-1',
        workspacePath: 'C:/repo',
        sourceWorkspacePath: 'C:/repo',
        path: 'C:/repo/.craft/worktrees/task-demo',
        branchName: 'dotcraft/task-demo',
        baseRef: 'HEAD',
        baseHead: 'abc123',
        head: 'def456',
        ownerKind: 'automationTask',
        ownerId: 'demo',
        createdAt: '2026-05-04T00:00:00Z'
      },
      path: 'C:/repo/.craft/worktrees/task-demo',
      branchName: 'dotcraft/task-demo',
      head: 'def456',
      exists: true,
      isGitWorktree: true,
      hasUncommittedChanges: true,
      hasCommitsAheadOfBase: true,
      aheadCount: 2
    }
    sendRequest.mockResolvedValueOnce({ status })

    const result = await useAutomationsStore.getState().getTaskWorktreeStatus({
      id: 'demo',
      title: 'Demo',
      status: 'completed',
      threadId: 'thread-1',
      workspaceMode: 'worktree',
      worktree: {
        branchName: 'dotcraft/task-demo',
        path: 'C:/repo/.craft/worktrees/task-demo'
      },
      createdAt: '2026-05-04T00:00:00Z',
      updatedAt: '2026-05-04T00:00:00Z'
    })

    expect(sendRequest).toHaveBeenCalledWith('worktree/status', {
      threadId: 'thread-1'
    })
    expect(result).toEqual(status)
  })

  it('discards a managed worktree and refreshes tasks silently', async () => {
    const task: AutomationTask = {
      id: 'demo',
      title: 'Demo',
      status: 'completed',
      threadId: 'thread-1',
      workspaceMode: 'worktree',
      worktree: {
        branchName: 'dotcraft/task-demo',
        path: 'C:/repo/.craft/worktrees/task-demo'
      },
      createdAt: '2026-05-04T00:00:00Z',
      updatedAt: '2026-05-04T00:00:00Z'
    }
    const updated = {
      ...task,
      worktree: null,
      updatedAt: '2026-05-04T00:01:00Z'
    }
    sendRequest
      .mockResolvedValueOnce({ task: updated })
      .mockResolvedValueOnce({ tasks: [updated] })

    const result = await useAutomationsStore.getState().discardTaskWorktree(task)

    expect(sendRequest).toHaveBeenNthCalledWith(
      1,
      'automation/task/discardWorktree',
      { taskId: 'demo' },
      180_000
    )
    expect(sendRequest).toHaveBeenNthCalledWith(2, 'automation/task/list', {})
    expect(result.worktree).toBeNull()
    expect(useAutomationsStore.getState().tasks[0]?.worktree).toBeNull()
  })

  it('does not expose approve or reject task actions', () => {
    const state = useAutomationsStore.getState() as unknown as Record<string, unknown>

    expect('approveTask' in state).toBe(false)
    expect('rejectTask' in state).toBe(false)
    expect('statusFilter' in state).toBe(false)
  })

  it('runs a local automation task now and refreshes silently', async () => {
    sendRequest
      .mockResolvedValueOnce({
        task: {
          id: 'weekly-report',
          title: 'Weekly report',
          status: 'pending',
          threadId: null,
          createdAt: '2026-05-04T00:00:00Z',
          updatedAt: '2026-05-04T00:00:00Z',
          nextRunAt: null
        }
      })
      .mockResolvedValueOnce({ tasks: [] })

    await useAutomationsStore.getState().runTaskNow({
      id: 'weekly-report',
      title: 'Weekly report',
      status: 'completed',
      threadId: null,
      createdAt: '2026-05-04T00:00:00Z',
      updatedAt: '2026-05-04T00:00:00Z'
    })

    expect(sendRequest).toHaveBeenNthCalledWith(1, 'automation/task/run', {
      taskId: 'weekly-report'
    })
    expect(sendRequest).toHaveBeenNthCalledWith(2, 'automation/task/list', {})
  })

  it('runs a cron job now and refreshes silently', async () => {
    sendRequest.mockResolvedValueOnce({ queued: true }).mockResolvedValueOnce({ jobs: [] })

    await useCronStore.getState().runJobNow('job-1')

    expect(sendRequest).toHaveBeenNthCalledWith(1, 'cron/run', { jobId: 'job-1' })
    expect(sendRequest).toHaveBeenNthCalledWith(2, 'cron/list', {
      includeDisabled: true
    })
  })

  it('does not send template-level default review fields when saving templates', async () => {
    sendRequest.mockResolvedValueOnce({
      template: {
        id: 'cleanup',
        title: 'Cleanup',
        workflowMarkdown: '---\n---'
      }
    })

    await useAutomationsStore.getState().saveTemplate({
      title: 'Cleanup',
      workflowMarkdown: '---\n---',
      defaultApprovalPolicy: 'workspaceScope',
      needsThreadBinding: false
    })

    expect(sendRequest).toHaveBeenCalledWith('automation/template/save', {
      title: 'Cleanup',
      workflowMarkdown: '---\n---',
      needsThreadBinding: false,
      defaultApprovalPolicy: 'workspaceScope'
    })
    expect(sendRequest.mock.calls[0][1]).not.toHaveProperty('defaultRequireApproval')
  })

  it('sends canonical worktree default when saving templates', async () => {
    sendRequest.mockResolvedValueOnce({
      template: {
        id: 'game',
        title: 'Game',
        workflowMarkdown: '---\nworkspace: worktree\n---'
      }
    })

    await useAutomationsStore.getState().saveTemplate({
      title: 'Game',
      workflowMarkdown: '---\nworkspace: worktree\n---',
      defaultWorkspaceMode: 'worktree',
      needsThreadBinding: false
    })

    expect(sendRequest).toHaveBeenCalledWith('automation/template/save', {
      title: 'Game',
      workflowMarkdown: '---\nworkspace: worktree\n---',
      needsThreadBinding: false,
      defaultWorkspaceMode: 'worktree'
    })
  })
})
