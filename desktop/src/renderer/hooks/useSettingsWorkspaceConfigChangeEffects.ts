import { useEffect, useRef } from 'react'
import { useConfigStore } from '../stores/configStore'
import {
  hasConfigKeyPathChange,
  type ConfigChangedPayload
} from '../utils/configChanged'

interface UseSettingsWorkspaceConfigChangeEffectsArgs {
  change: ConfigChangedPayload | null
  changeSeq: number
  mcpEnabled: boolean
  subAgentEnabled?: boolean
  reloadDreamsStatus?: () => Promise<void> | void
  reloadMcpData: () => Promise<void> | void
  reloadSubAgentData?: () => Promise<void> | void
}

export function useSettingsWorkspaceConfigChangeEffects({
  change,
  changeSeq,
  mcpEnabled,
  subAgentEnabled = false,
  reloadDreamsStatus,
  reloadMcpData,
  reloadSubAgentData
}: UseSettingsWorkspaceConfigChangeEffectsArgs): void {
  const lastHandledSeqRef = useRef(changeSeq)

  useEffect(() => {
    if (change == null || changeSeq === 0 || changeSeq <= lastHandledSeqRef.current) {
      return
    }

    lastHandledSeqRef.current = changeSeq

    const changedRegions = new Set(change.regions)
    if (changedRegions.has('providers') || changedRegions.has('memory')) {
      void useConfigStore.getState().refresh()
    }
    if (changedRegions.has('memory') || hasConfigKeyPathChange(change.regions, 'Memory', 'Dreams')) {
      void reloadDreamsStatus?.()
    }

    if (changedRegions.has('mcp') && mcpEnabled) {
      void reloadMcpData()
    }

    if (changedRegions.has('subagent') && subAgentEnabled) {
      void reloadSubAgentData?.()
    }

  }, [
    change,
    changeSeq,
    mcpEnabled,
    reloadDreamsStatus,
    reloadMcpData,
    reloadSubAgentData,
    subAgentEnabled
  ])
}
