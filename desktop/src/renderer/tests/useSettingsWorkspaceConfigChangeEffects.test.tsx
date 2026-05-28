import { render, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useSettingsWorkspaceConfigChangeEffects } from '../hooks/useSettingsWorkspaceConfigChangeEffects'
import type { WorkspaceConfigChangedPayload } from '../utils/workspaceConfigChanged'

function HookHost(props: {
  change: WorkspaceConfigChangedPayload | null
  changeSeq: number
  llmDirty?: boolean
  mcpEnabled?: boolean
  subAgentEnabled?: boolean
  onExternalLlmChangeNotice?: () => void
  reloadWorkspaceCore?: () => Promise<void> | void
  reloadDreamsStatus?: () => Promise<void> | void
  reloadMcpData?: () => Promise<void> | void
  reloadSubAgentData?: () => Promise<void> | void
}): JSX.Element {
  useSettingsWorkspaceConfigChangeEffects({
    change: props.change,
    changeSeq: props.changeSeq,
    llmDirty: props.llmDirty ?? false,
    mcpEnabled: props.mcpEnabled ?? false,
    subAgentEnabled: props.subAgentEnabled ?? false,
    onExternalLlmChangeNotice: props.onExternalLlmChangeNotice ?? vi.fn(),
    reloadWorkspaceCore: props.reloadWorkspaceCore ?? vi.fn(),
    reloadDreamsStatus: props.reloadDreamsStatus,
    reloadMcpData: props.reloadMcpData ?? vi.fn(),
    reloadSubAgentData: props.reloadSubAgentData ?? vi.fn()
  })

  return <div />
}

describe('useSettingsWorkspaceConfigChangeEffects', () => {
  it('does not replay an already-seen event on initial mount', () => {
    const reloadWorkspaceCore = vi.fn()

    render(
      <HookHost
        change={{
          source: 'workspace/config/update',
          regions: ['workspace.model'],
          changedAt: '2026-04-19T10:15:03Z'
        }}
        changeSeq={1}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    expect(reloadWorkspaceCore).not.toHaveBeenCalled()
  })

  it('reloads workspace core once and shows a single external-change notice', async () => {
    const onExternalLlmChangeNotice = vi.fn()
    const reloadWorkspaceCore = vi.fn()
    const { rerender } = render(
      <HookHost
        change={null}
        changeSeq={0}
        llmDirty={true}
        onExternalLlmChangeNotice={onExternalLlmChangeNotice}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    rerender(
      <HookHost
        change={{
          source: 'manual-edit',
          regions: ['workspace.model', 'workspace.provider'],
          changedAt: '2026-04-19T10:15:03Z'
        }}
        changeSeq={1}
        llmDirty={true}
        onExternalLlmChangeNotice={onExternalLlmChangeNotice}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    await waitFor(() => {
      expect(onExternalLlmChangeNotice).toHaveBeenCalledTimes(1)
      expect(reloadWorkspaceCore).toHaveBeenCalledTimes(1)
    })
  })

  it('refreshes MCP data from incoming config events', async () => {
    const reloadMcpData = vi.fn()
    const { rerender } = render(
      <HookHost
        change={null}
        changeSeq={0}
        mcpEnabled={true}
        reloadMcpData={reloadMcpData}
      />
    )

    rerender(
      <HookHost
        change={{
          source: 'workspace/config/update',
          regions: ['mcp', 'externalChannel'],
          changedAt: '2026-04-19T10:15:03Z'
        }}
        changeSeq={1}
        mcpEnabled={true}
        reloadMcpData={reloadMcpData}
      />
    )

    await waitFor(() => {
      expect(reloadMcpData).toHaveBeenCalledTimes(1)
    })
  })

  it('refreshes subagent data from incoming config events', async () => {
    const reloadSubAgentData = vi.fn()
    const { rerender } = render(
      <HookHost
        change={null}
        changeSeq={0}
        subAgentEnabled={true}
        reloadSubAgentData={reloadSubAgentData}
      />
    )

    rerender(
      <HookHost
        change={{
          source: 'subagent/profiles/upsert',
          regions: ['subagent'],
          changedAt: '2026-04-21T10:15:03Z'
        }}
        changeSeq={1}
        subAgentEnabled={true}
        reloadSubAgentData={reloadSubAgentData}
      />
    )

    await waitFor(() => {
      expect(reloadSubAgentData).toHaveBeenCalledTimes(1)
    })
  })

  it('reloads workspace core when welcome suggestions config changes', async () => {
    const reloadWorkspaceCore = vi.fn()
    const { rerender } = render(
      <HookHost
        change={null}
        changeSeq={0}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    rerender(
      <HookHost
        change={{
          source: 'workspace/config/update',
          regions: ['welcomeSuggestions'],
          changedAt: '2026-04-19T10:15:03Z'
        }}
        changeSeq={1}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    await waitFor(() => {
      expect(reloadWorkspaceCore).toHaveBeenCalledTimes(1)
    })
  })

  it('does not show LLM external-change notice when only welcome suggestions changed', async () => {
    const onExternalLlmChangeNotice = vi.fn()
    const reloadWorkspaceCore = vi.fn()
    const { rerender } = render(
      <HookHost
        change={null}
        changeSeq={0}
        llmDirty={true}
        onExternalLlmChangeNotice={onExternalLlmChangeNotice}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    rerender(
      <HookHost
        change={{
          source: 'manual-edit',
          regions: ['welcomeSuggestions'],
          changedAt: '2026-04-19T10:15:03Z'
        }}
        changeSeq={1}
        llmDirty={true}
        onExternalLlmChangeNotice={onExternalLlmChangeNotice}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    await waitFor(() => {
      expect(reloadWorkspaceCore).toHaveBeenCalledTimes(1)
    })
    expect(onExternalLlmChangeNotice).not.toHaveBeenCalled()
  })

  it('reloads workspace core and Dreams status when memory config changes', async () => {
    const reloadWorkspaceCore = vi.fn()
    const reloadDreamsStatus = vi.fn()
    const { rerender } = render(
      <HookHost
        change={null}
        changeSeq={0}
        reloadWorkspaceCore={reloadWorkspaceCore}
        reloadDreamsStatus={reloadDreamsStatus}
      />
    )

    rerender(
      <HookHost
        change={{
          source: 'workspace/config/update',
          regions: ['memory'],
          changedAt: '2026-04-19T10:15:03Z'
        }}
        changeSeq={1}
        reloadWorkspaceCore={reloadWorkspaceCore}
        reloadDreamsStatus={reloadDreamsStatus}
      />
    )

    await waitFor(() => {
      expect(reloadWorkspaceCore).toHaveBeenCalledTimes(1)
      expect(reloadDreamsStatus).toHaveBeenCalledTimes(1)
    })
  })

  it('reloads workspace core when default approval policy changes', async () => {
    const reloadWorkspaceCore = vi.fn()
    const { rerender } = render(
      <HookHost
        change={null}
        changeSeq={0}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    rerender(
      <HookHost
        change={{
          source: 'workspace/config/update',
          regions: ['workspace.defaultApprovalPolicy'],
          changedAt: '2026-04-19T10:15:03Z'
        }}
        changeSeq={1}
        reloadWorkspaceCore={reloadWorkspaceCore}
      />
    )

    await waitFor(() => {
      expect(reloadWorkspaceCore).toHaveBeenCalledTimes(1)
    })
  })
})
