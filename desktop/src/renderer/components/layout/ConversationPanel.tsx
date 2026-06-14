import { useCallback, useEffect, useMemo, useState } from 'react'
import { useThreadStore } from '../../stores/threadStore'
import { selectLatestCreatePlanTurnId, useConversationStore, type PendingApproval } from '../../stores/conversationStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useModelCatalogStore, type ReasoningEffortWire, type ReasoningOutputWire } from '../../stores/modelCatalogStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { ThreadHeader } from '../conversation/ThreadHeader'
import { InteractiveToolOverlay } from '../conversation/InteractiveToolView'
import { MessageStream } from '../conversation/MessageStream'
import { InputComposer } from '../conversation/InputComposer'
import { PlanApprovalComposer } from '../conversation/PlanApprovalComposer'
import { RequestUserInputComposer } from '../conversation/RequestUserInputComposer'
import { ApprovalDecisionComposer } from '../conversation/ApprovalDecisionComposer'
import { ConversationWelcome } from '../conversation/ConversationWelcome'
import type { ThreadConfigurationWire } from '../../types/thread'
import type { ReasoningQuickValue } from '../conversation/ModelPicker'
import { parseJsonConfig } from '../../../shared/jsonConfig'
import type { WorkspaceConfigChangedPayload } from '../../utils/workspaceConfigChanged'
import { configObjectFromWorkspaceCore, type WorkspaceCoreConfigLike } from '../../utils/workspaceCoreConfig'

interface ConversationPanelProps {
  workspacePath?: string
  identityWorkspacePath?: string
  projectKey?: string
  remoteWorkspace?: boolean
  workspaceConfigChange?: WorkspaceConfigChangedPayload | null
  workspaceConfigChangeSeq?: number
  onInteractionResponseAccepted?: () => void
  /** Render the composer with minimal chrome (no workspace/branch footer, permissions, or subscription badge). */
  minimalComposer?: boolean
}

interface ResolvedReasoningConfig {
  enabled: boolean
  effort: ReasoningEffortWire
  output: ReasoningOutputWire
}

const DEFAULT_REASONING_CONFIG: ResolvedReasoningConfig = {
  enabled: false,
  effort: 'medium',
  output: 'full'
}

function approvalComposerKey(request: PendingApproval): string {
  return `${request.source ?? 'tool'}:${request.requestId || request.itemId || request.bridgeId}`
}

/**
 * Main conversation panel.
 * Composes: ThreadHeader, MessageStream, InputComposer.
 * Spec §10
 */
export function ConversationPanel({
  workspacePath = '',
  identityWorkspacePath,
  projectKey,
  remoteWorkspace = false,
  workspaceConfigChange = null,
  workspaceConfigChangeSeq = 0,
  onInteractionResponseAccepted,
  minimalComposer = false
}: ConversationPanelProps): JSX.Element {
  const activeThread = useThreadStore((s) => s.activeThread)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const loading = useThreadStore((s) => s.loading)
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const threadMode = useConversationStore((s) => s.threadMode)
  const pendingApproval = useConversationStore((s) => s.pendingApproval)
  const genericApproval = useConversationStore((s) => s.genericApproval)
  // Tool approvals (turn-bound) take priority over turn-less approvals (e.g. browser-use).
  const composerApproval = pendingApproval ?? genericApproval
  const pendingUserInput = useConversationStore((s) => s.pendingUserInput)
  const latestCreatePlanTurnId = useConversationStore(selectLatestCreatePlanTurnId)
  const connectionStatus = useConnectionStore((s) => s.status)
  const connectionErrorMessage = useConnectionStore((s) => s.errorMessage)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const planApprovalDismissed = useUIStore((s) => s.planApprovalDismissed)
  const resetPlanApprovalDismissed = useUIStore((s) => s.resetPlanApprovalDismissed)
  const modelCatalog = useModelCatalogStore((s) => s.models)
  const modelOptions = useModelCatalogStore((s) => s.modelOptions)
  const modelCatalogStatus = useModelCatalogStore((s) => s.status)
  const modelListUnsupportedEndpoint = useModelCatalogStore((s) => s.modelListUnsupportedEndpoint)
  const modelCatalogErrorCode = useModelCatalogStore((s) => s.errorCode)
  const modelCatalogErrorMessage = useModelCatalogStore((s) => s.errorMessage)
  const loadModels = useModelCatalogStore((s) => s.loadIfNeeded)
  const protocolWorkspacePath = identityWorkspacePath || workspacePath
  const threadStateWorkspacePath = activeThread?.workspacePath || protocolWorkspacePath
  const activeEffectiveWorkspacePath =
    activeThread?.effectiveWorkspacePath?.trim() || threadStateWorkspacePath
  const [modelName, setModelName] = useState<string>('Default')
  const [reasoningConfig, setReasoningConfig] = useState<ResolvedReasoningConfig>(DEFAULT_REASONING_CONFIG)
  const [modelApplying, setModelApplying] = useState(false)

  const showReconnectionBanner = connectionStatus === 'disconnected'
  const modelApiAvailable =
    capabilities?.modelCatalogManagement === true &&
    capabilities?.workspaceConfigManagement === true &&
    connectionStatus === 'connected' &&
    Boolean(activeThreadId)
  const modelLoading = modelApiAvailable && modelCatalogStatus === 'loading'
  const showPlanApproval = threadMode === 'plan'
    && turnStatus === 'idle'
    && composerApproval == null
    && latestCreatePlanTurnId != null
    && planApprovalDismissed[latestCreatePlanTurnId] !== true

  const workspaceConfigPath = useMemo(() => {
    if (!workspacePath) return ''
    const normalized = workspacePath.replace(/[\\/]+$/, '')
    const sep = normalized.includes('\\') ? '\\' : '/'
    return `${normalized}${sep}.craft${sep}config.json`
  }, [workspacePath])

  const readWorkspaceConfig = useCallback(async (): Promise<Record<string, unknown>> => {
    if (remoteWorkspace) {
      const getCore = window.api.workspaceConfig?.getCore
      if (typeof getCore !== 'function') return {}
      return configObjectFromWorkspaceCore(await getCore() as WorkspaceCoreConfigLike)
    }
    if (!workspaceConfigPath) return {}
    const raw = await window.api.file.readFile(workspaceConfigPath)
    return parseJsonConfig<Record<string, unknown>>(raw, {})
  }, [remoteWorkspace, workspaceConfigPath])

  const setCaseInsensitiveField = useCallback(
    (target: Record<string, unknown>, key: string, value: unknown): void => {
      const lower = key.toLowerCase()
      const existingKey = Object.keys(target).find((k) => k.toLowerCase() === lower)
      if (existingKey) {
        target[existingKey] = value
      } else {
        target[key] = value
      }
    },
    []
  )

  const deleteCaseInsensitiveField = useCallback((target: Record<string, unknown>, key: string): void => {
    const lower = key.toLowerCase()
    const existingKey = Object.keys(target).find((k) => k.toLowerCase() === lower)
    if (existingKey) delete target[existingKey]
  }, [])

  const resolveEffectiveModel = useCallback(
    (thread: typeof activeThread, workspaceCfg: Record<string, unknown>): string => {
      const workspaceModelRaw = workspaceCfg.Model ?? workspaceCfg.model
      const ws =
        typeof workspaceModelRaw === 'string' ? workspaceModelRaw.trim() : ''
      const workspaceModel =
        ws.length > 0 && ws !== 'Default' ? ws : null
      const threadRaw = thread?.configuration?.model ?? thread?.configuration?.Model
      const threadTrimmed = typeof threadRaw === 'string' ? threadRaw.trim() : ''
      if (threadTrimmed.length > 0 && threadTrimmed !== 'Default') {
        return threadTrimmed
      }
      return workspaceModel ?? 'Default'
    },
    []
  )

  const resolveEffectiveReasoning = useCallback(
    (thread: typeof activeThread, workspaceCfg: Record<string, unknown>): ResolvedReasoningConfig => {
      const threadReasoning = readReasoningObject(thread?.configuration?.reasoning ?? thread?.configuration?.Reasoning)
      if (threadReasoning) return threadReasoning
      const workspaceReasoning = readReasoningObject(workspaceCfg.Reasoning ?? workspaceCfg.reasoning)
      return workspaceReasoning ?? DEFAULT_REASONING_CONFIG
    },
    []
  )

  useEffect(() => {
    let disposed = false
    const loadEffectiveModel = async (): Promise<void> => {
      try {
        const workspaceCfg = await readWorkspaceConfig()
        if (disposed) return
        setModelName(resolveEffectiveModel(activeThread, workspaceCfg))
        setReasoningConfig(resolveEffectiveReasoning(activeThread, workspaceCfg))
      } catch {
        if (disposed) return
        const modelFromThread = activeThread?.configuration?.model ?? activeThread?.configuration?.Model
        const mt = typeof modelFromThread === 'string' ? modelFromThread.trim() : ''
        setModelName(mt.length > 0 && mt !== 'Default' ? mt : 'Default')
        setReasoningConfig(readReasoningObject(activeThread?.configuration?.reasoning ?? activeThread?.configuration?.Reasoning) ?? DEFAULT_REASONING_CONFIG)
      }
    }

    void loadEffectiveModel()
    return () => {
      disposed = true
    }
  }, [
    activeThreadId,
    activeThread?.configuration?.Model,
    activeThread?.configuration?.model,
    activeThread?.configuration?.Reasoning,
    activeThread?.configuration?.reasoning,
    readWorkspaceConfig,
    resolveEffectiveModel,
    resolveEffectiveReasoning,
    workspaceConfigChange,
    workspaceConfigChangeSeq
  ])

  useEffect(() => {
    resetPlanApprovalDismissed()
  }, [activeThreadId, resetPlanApprovalDismissed])

  const handleModelChange = useCallback(
    async (nextModel: string): Promise<void> => {
      if (!activeThread || !nextModel || nextModel === modelName) return
      setModelApplying(true)
      const previousModel = modelName
      setModelName(nextModel)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          model: nextModel === 'Default' ? null : nextModel
        })

        const readRes = (await window.api.appServer.sendRequest('thread/read', {
          threadId: activeThread.id,
          includeTurns: false
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig =
          readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
            ? { ...(readRes.thread.configuration as Record<string, unknown>) }
            : {}
        if (nextModel === 'Default') {
          deleteCaseInsensitiveField(existingConfig, 'model')
        } else {
          setCaseInsensitiveField(existingConfig, 'model', nextModel)
        }

        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })
        const active = useThreadStore.getState().activeThread
        if (active && active.id === activeThread.id) {
          const mergedCfg: Record<string, unknown> = { ...(active.configuration ?? {}) }
          if (nextModel === 'Default') {
            deleteCaseInsensitiveField(mergedCfg, 'model')
          } else {
            mergedCfg.model = nextModel
          }
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: mergedCfg as typeof active.configuration
          })
        }
        addToast(
          nextModel === 'Default' ? 'Using workspace default model' : `Model switched to ${nextModel}`,
          'success'
        )
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setModelName(previousModel)
        addToast(`Failed to switch model: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [
      activeThread,
      deleteCaseInsensitiveField,
      modelName,
      setCaseInsensitiveField,
    ]
  )

  const handleReasoningChange = useCallback(
    async (nextReasoning: ReasoningQuickValue): Promise<void> => {
      if (!activeThread) return
      const nextPayload = buildReasoningPayload(nextReasoning, reasoningConfig)
      setModelApplying(true)
      const previousReasoning = reasoningConfig
      setReasoningConfig(nextPayload ?? DEFAULT_REASONING_CONFIG)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          reasoning: nextPayload
        })

        const readRes = (await window.api.appServer.sendRequest('thread/read', {
          threadId: activeThread.id,
          includeTurns: false
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig =
          readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
            ? { ...(readRes.thread.configuration as Record<string, unknown>) }
            : {}
        if (nextReasoning === 'default') {
          deleteCaseInsensitiveField(existingConfig, 'reasoning')
        } else {
          setCaseInsensitiveField(existingConfig, 'reasoning', nextPayload)
        }

        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })
        const active = useThreadStore.getState().activeThread
        if (active && active.id === activeThread.id) {
          const mergedCfg: Record<string, unknown> = { ...(active.configuration ?? {}) }
          if (nextReasoning === 'default') {
            deleteCaseInsensitiveField(mergedCfg, 'reasoning')
          } else {
            mergedCfg.reasoning = nextPayload
          }
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: mergedCfg as typeof active.configuration
          })
        }
        addToast(
          nextReasoning === 'default'
            ? 'Using default thinking setting'
            : `Thinking set to ${reasoningQuickToastLabel(nextReasoning)}`,
          'success'
        )
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setReasoningConfig(previousReasoning)
        addToast(`Failed to update thinking: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [
      activeThread,
      deleteCaseInsensitiveField,
      reasoningConfig,
      setCaseInsensitiveField,
    ]
  )

  // Loading state: thread selected but full data not yet fetched
  if (activeThreadId && !activeThread && loading) {
    return (
      <div style={centeredStyle}>
        <span style={{ color: 'var(--text-dimmed)', fontSize: '13px' }}>Loading thread...</span>
      </div>
    )
  }

  // No thread selected — show the welcome card
  if (!activeThread) {
    return (
      <ConversationWelcome
        workspacePath={workspacePath}
        identityWorkspacePath={protocolWorkspacePath}
        projectKey={projectKey}
        remoteWorkspace={remoteWorkspace}
        workspaceConfigChange={workspaceConfigChange}
        workspaceConfigChangeSeq={workspaceConfigChangeSeq}
      />
    )
  }

  const threadName = activeThread.displayName ?? 'New conversation'
  const hasContent = turns.length > 0 || turnStatus === 'running'

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        background: 'transparent',
        overflow: 'hidden'
      }}
    >
      {/* Interactive Tool UI expanded surface (pip/fullscreen) — portals to body when a card is expanded. */}
      <InteractiveToolOverlay />

      {/* Fixed header */}
      <ThreadHeader
        threadName={threadName}
        threadId={activeThread.id}
        workspacePath={activeEffectiveWorkspacePath}
        remoteWorkspace={remoteWorkspace}
      />

      {/* Reconnection banner */}
      {showReconnectionBanner && (
        <div
          role="status"
          aria-live="polite"
          style={{
            padding: '8px 16px',
            backgroundColor: 'rgba(220,38,38,0.1)',
            borderBottom: '1px solid var(--error)',
            color: 'var(--error)',
            fontSize: '12px',
            fontWeight: 500,
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            flexShrink: 0
          }}
        >
          <span style={{ width: '7px', height: '7px', borderRadius: '50%', background: 'var(--error)', flexShrink: 0, animation: 'pulse 1.5s ease-in-out infinite' }} />
          {connectionErrorMessage || 'Connection lost. Reconnecting...'}
        </div>
      )}

      {/* Archived thread notice — spec §18.2 */}
      {activeThread.status === 'archived' && (
        <div
          role="status"
          style={{
            padding: '8px 16px',
            backgroundColor: 'rgba(160,160,160,0.1)',
            borderBottom: '1px solid var(--border-default)',
            color: 'var(--text-secondary)',
            fontSize: '12px',
            fontWeight: 500,
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            flexShrink: 0
          }}
        >
          This thread has been archived.
        </div>
      )}

      {/* Message stream (fills remaining space) */}
      {hasContent ? (
        <MessageStream />
      ) : (
        <div style={centeredStyle}>
          <p style={{ fontSize: '14px', color: 'var(--text-dimmed)', margin: 0, textAlign: 'center' }}>
            Type a message below to get started.
          </p>
        </div>
      )}

      {/* Input composer */}
      {composerApproval ? (
        <ApprovalDecisionComposer
          key={approvalComposerKey(composerApproval)}
          request={composerApproval}
          onResponseAccepted={onInteractionResponseAccepted}
        />
      ) : pendingUserInput ? (
        <RequestUserInputComposer
          request={pendingUserInput}
          onResponseAccepted={onInteractionResponseAccepted}
        />
      ) : showPlanApproval && latestCreatePlanTurnId ? (
        <PlanApprovalComposer
          threadId={activeThread.id}
          workspacePath={protocolWorkspacePath}
          turnId={latestCreatePlanTurnId}
        />
      ) : (
        <InputComposer
          threadId={activeThread.id}
          workspacePath={threadStateWorkspacePath}
          fileWorkspacePath={activeEffectiveWorkspacePath}
          remoteWorkspace={remoteWorkspace}
          minimalChrome={minimalComposer}
          modelName={modelName}
          modelOptions={modelOptions}
          modelCatalog={modelCatalog}
          reasoningValue={reasoningConfig.enabled ? reasoningConfig.effort : 'off'}
          modelLoading={modelLoading}
          modelDisabled={modelApplying || !modelApiAvailable}
          modelListUnsupportedEndpoint={modelListUnsupportedEndpoint}
          modelCatalogError={modelCatalogStatus === 'error'}
          modelCatalogErrorMessage={
            modelCatalogStatus === 'error' && modelCatalogErrorCode
              ? `${modelCatalogErrorCode}: ${modelCatalogErrorMessage ?? ''}`.trim()
              : modelCatalogErrorMessage
          }
          onModelChange={(m) => {
            void handleModelChange(m)
          }}
          onReasoningChange={(r) => {
            void handleReasoningChange(r)
          }}
          onModelCatalogRetry={() => {
            void loadModels(true)
          }}
        />
      )}
    </div>
  )
}

const centeredStyle: React.CSSProperties = {
  display: 'flex',
  flex: 1,
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'transparent'
}

function buildReasoningPayload(
  value: ReasoningQuickValue,
  current: ResolvedReasoningConfig
): ResolvedReasoningConfig | null {
  if (value === 'default') return null
  if (value === 'off') {
    return {
      enabled: false,
      effort: current.effort || 'medium',
      output: current.output || 'full'
    }
  }
  return {
    enabled: true,
    effort: value,
    output: current.output || 'full'
  }
}

function readReasoningObject(value: unknown): ResolvedReasoningConfig | null {
  if (!value || typeof value !== 'object') return null
  const obj = value as Record<string, unknown>
  const enabledRaw = obj.enabled ?? obj.Enabled
  const effort = normalizeReasoningEffort(obj.effort ?? obj.Effort)
  const output = normalizeReasoningOutput(obj.output ?? obj.Output)
  return {
    enabled: typeof enabledRaw === 'boolean' ? enabledRaw : false,
    effort: effort ?? 'medium',
    output: output ?? 'full'
  }
}

function normalizeReasoningEffort(value: unknown): ReasoningEffortWire | null {
  if (typeof value !== 'string') return null
  const normalized = value.replace(/[-_\s]/g, '').toLowerCase()
  if (normalized === 'low') return 'low'
  if (normalized === 'medium') return 'medium'
  if (normalized === 'high') return 'high'
  if (normalized === 'extrahigh') return 'extraHigh'
  return null
}

function normalizeReasoningOutput(value: unknown): ReasoningOutputWire | null {
  if (typeof value !== 'string') return null
  const normalized = value.trim().toLowerCase()
  if (normalized === 'none') return 'none'
  if (normalized === 'summary') return 'summary'
  if (normalized === 'full') return 'full'
  return null
}

function reasoningQuickToastLabel(value: ReasoningQuickValue): string {
  if (value === 'off') return 'Off'
  if (value === 'low') return 'Low'
  if (value === 'medium') return 'Medium'
  if (value === 'high') return 'High'
  if (value === 'extraHigh') return 'Extra High'
  return 'Default'
}
