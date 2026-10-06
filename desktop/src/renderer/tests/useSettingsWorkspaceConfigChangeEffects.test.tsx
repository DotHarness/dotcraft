import { render, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSettingsWorkspaceConfigChangeEffects } from '../hooks/useSettingsWorkspaceConfigChangeEffects'
import { useConfigStore } from '../stores/configStore'
import type { ConfigChangedPayload } from '../utils/configChanged'

function HookHost(props: {
  change: ConfigChangedPayload | null
  changeSeq: number
  mcpEnabled?: boolean
  subAgentEnabled?: boolean
  reloadDreamsStatus?: () => Promise<void> | void
  reloadMcpData?: () => Promise<void> | void
  reloadSubAgentData?: () => Promise<void> | void
}): JSX.Element {
  useSettingsWorkspaceConfigChangeEffects({
    change: props.change,
    changeSeq: props.changeSeq,
    mcpEnabled: props.mcpEnabled ?? false,
    subAgentEnabled: props.subAgentEnabled ?? false,
    reloadDreamsStatus: props.reloadDreamsStatus,
    reloadMcpData: props.reloadMcpData ?? vi.fn(),
    reloadSubAgentData: props.reloadSubAgentData ?? vi.fn()
  })

  return <div />
}

const refreshConfig = vi.fn()

beforeEach(() => {
  refreshConfig.mockReset()
  useConfigStore.setState({ refresh: refreshConfig })
})

describe('useSettingsWorkspaceConfigChangeEffects', () => {
  it('does not replay an already-seen event on initial mount', () => {
    const reloadDreamsStatus = vi.fn()

    render(
      <HookHost
        change={{
          source: 'config/value/write',
          regions: ['Dreams.Enabled'],
          changedAt: '2026-04-19T10:15:03Z'
        }}
        changeSeq={1}
        reloadDreamsStatus={reloadDreamsStatus}
      />
    )

    expect(reloadDreamsStatus).not.toHaveBeenCalled()
    expect(refreshConfig).not.toHaveBeenCalled()
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
          source: 'mcp/upsert',
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

  it('refreshes configuration when providers change', async () => {
    const { rerender } = render(<HookHost change={null} changeSeq={0} />)

    rerender(
      <HookHost
        change={{ source: 'provider/update', regions: ['providers'], changedAt: '2026-04-19T10:15:03Z' }}
        changeSeq={1}
      />
    )

    await waitFor(() => {
      expect(refreshConfig).toHaveBeenCalledTimes(1)
    })
  })

  it('reloads Dreams status when a Dreams or memory key path changes', async () => {
    const reloadDreamsStatus = vi.fn()
    const { rerender } = render(<HookHost change={null} changeSeq={0} reloadDreamsStatus={reloadDreamsStatus} />)

    rerender(
      <HookHost
        change={{ source: 'config/value/write', regions: ['Dreams.Interval'], changedAt: '2026-04-19T10:15:03Z' }}
        changeSeq={1}
        reloadDreamsStatus={reloadDreamsStatus}
      />
    )
    rerender(
      <HookHost
        change={{ source: 'config/value/write', regions: ['Memory.Enabled'], changedAt: '2026-04-19T10:15:04Z' }}
        changeSeq={2}
        reloadDreamsStatus={reloadDreamsStatus}
      />
    )

    await waitFor(() => {
      expect(reloadDreamsStatus).toHaveBeenCalledTimes(2)
    })
    expect(refreshConfig).not.toHaveBeenCalled()
  })

  it('refreshes configuration and Dreams status on the memory domain tag', async () => {
    const reloadDreamsStatus = vi.fn()
    const { rerender } = render(<HookHost change={null} changeSeq={0} reloadDreamsStatus={reloadDreamsStatus} />)

    rerender(
      <HookHost
        change={{ source: 'memory/reset', regions: ['memory'], changedAt: '2026-04-19T10:15:03Z' }}
        changeSeq={1}
        reloadDreamsStatus={reloadDreamsStatus}
      />
    )

    await waitFor(() => {
      expect(refreshConfig).toHaveBeenCalledTimes(1)
      expect(reloadDreamsStatus).toHaveBeenCalledTimes(1)
    })
  })
})
