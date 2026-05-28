import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ApprovalPolicyPicker } from '../components/conversation/ApprovalPolicyPicker'
import { useThreadStore } from '../stores/threadStore'

const appServerSendRequest = vi.fn()
const workspaceConfigGetCore = vi.fn()

function renderPicker(disabled = false): void {
  render(
    <LocaleProvider>
      <ApprovalPolicyPicker threadId="thread_1" disabled={disabled} />
    </LocaleProvider>
  )
}

describe('ApprovalPolicyPicker', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    delete (window as Window & { __confirmDialog?: unknown }).__confirmDialog
    useThreadStore.getState().reset()
    useThreadStore.getState().setActiveThread({
      id: 'thread_1',
      displayName: null,
      status: 'active',
      originChannel: 'dotcraft-desktop',
      createdAt: new Date().toISOString(),
      lastActiveAt: new Date().toISOString(),
      workspacePath: 'E:\\Git\\dotcraft',
      userId: 'local',
      metadata: {},
      configuration: { approvalPolicy: 'default' },
      turns: []
    })
    workspaceConfigGetCore.mockResolvedValue({
      workspace: { defaultApprovalPolicy: 'autoApprove' },
      userDefaults: { defaultApprovalPolicy: null }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/read') {
        return { thread: { configuration: { mode: 'agent' } } }
      }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: vi.fn().mockResolvedValue({ locale: 'en' })
        },
        workspaceConfig: { getCore: workspaceConfigGetCore },
        appServer: { sendRequest: appServerSendRequest }
      }
    })
  })

  it('shows the current approval label and workspace-default hint', async () => {
    renderPicker()

    const trigger = await screen.findByTestId('approval-policy-trigger')
    expect(trigger).toHaveTextContent('Default permissions')
    expect(screen.getByTestId('approval-policy-icon-default')).toBeInTheDocument()

    await waitFor(() => {
      expect(trigger.closest('div')).toHaveAttribute('title', 'Workspace default: Full access')
    })
  })

  it('warns before enabling full access and merges thread config update', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm

    renderPicker()

    fireEvent.click(await screen.findByTestId('approval-policy-trigger'))
    fireEvent.click(await screen.findByTestId('approval-policy-option-autoApprove'))

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/config/update', {
        threadId: 'thread_1',
        config: {
          mode: 'agent',
          approvalPolicy: 'autoApprove'
        }
      })
    })
    expect(useThreadStore.getState().activeThread?.configuration?.approvalPolicy).toBe('autoApprove')
  })

  it('does not update when the warning is cancelled', async () => {
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = vi.fn().mockResolvedValue(false)

    renderPicker()

    fireEvent.click(await screen.findByTestId('approval-policy-trigger'))
    fireEvent.click(await screen.findByTestId('approval-policy-option-autoApprove'))

    await waitFor(() => {
      expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/config/update', expect.anything())
    })
    expect(useThreadStore.getState().activeThread?.configuration?.approvalPolicy).toBe('default')
  })

  it('is disabled while approvals or turns block config changes', async () => {
    renderPicker(true)

    expect(await screen.findByTestId('approval-policy-trigger')).toBeDisabled()
  })

  it('supports controlled welcome mode without writing thread config directly', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    const onChange = vi.fn()
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm

    render(
      <LocaleProvider>
        <ApprovalPolicyPicker value="default" onChange={onChange} />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByTestId('approval-policy-trigger'))
    fireEvent.click(await screen.findByTestId('approval-policy-option-autoApprove'))

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
      expect(onChange).toHaveBeenCalledWith('autoApprove')
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/config/update', expect.anything())
  })
})
