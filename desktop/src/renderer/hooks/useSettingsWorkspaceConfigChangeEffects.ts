import { useEffect, useRef } from 'react'
import {
  WORKSPACE_DEFAULT_APPROVAL_POLICY_REGION,
  type WorkspaceConfigChangedPayload
} from '../utils/workspaceConfigChanged'

interface UseSettingsWorkspaceConfigChangeEffectsArgs {
  change: WorkspaceConfigChangedPayload | null
  changeSeq: number
  llmDirty: boolean
  mcpEnabled: boolean
  subAgentEnabled?: boolean
  onExternalLlmChangeNotice: () => void
  reloadWorkspaceCore: () => Promise<void> | void
  reloadDreamsStatus?: () => Promise<void> | void
  reloadMcpData: () => Promise<void> | void
  reloadSubAgentData?: () => Promise<void> | void
}

export function useSettingsWorkspaceConfigChangeEffects({
  change,
  changeSeq,
  llmDirty,
  mcpEnabled,
  subAgentEnabled = false,
  onExternalLlmChangeNotice,
  reloadWorkspaceCore,
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
    const llmCoreChanged =
      changedRegions.has('workspace.providerPreferences') ||
      changedRegions.has('workspace.provider') ||
      changedRegions.has('providers')
    const workspaceCoreChanged =
      llmCoreChanged ||
      changedRegions.has('welcomeSuggestions') ||
      changedRegions.has('memory') ||
      changedRegions.has(WORKSPACE_DEFAULT_APPROVAL_POLICY_REGION)

    if (workspaceCoreChanged) {
      if (llmCoreChanged && llmDirty && change.source !== 'workspace/config/update') {
        onExternalLlmChangeNotice()
      }
      void reloadWorkspaceCore()
      if (changedRegions.has('memory')) {
        void reloadDreamsStatus?.()
      }
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
    llmDirty,
    mcpEnabled,
    onExternalLlmChangeNotice,
    reloadDreamsStatus,
    reloadMcpData,
    reloadSubAgentData,
    reloadWorkspaceCore,
    subAgentEnabled
  ])
}
