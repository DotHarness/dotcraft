import { useEffect, useState, type CSSProperties } from 'react'
import { useThreadStore } from '../../stores/threadStore'
import { selectLatestCreatePlanTurnId, useConversationStore, type PendingApproval } from '../../stores/conversationStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { ThreadHeader } from '../conversation/ThreadHeader'
import { MessageStream } from '../conversation/MessageStream'
import { InputComposer } from '../conversation/InputComposer'
import type { AvatarSpec } from '../agents/agentAvatar'
import { PlanApprovalComposer } from '../conversation/PlanApprovalComposer'
import { RequestUserInputComposer } from '../conversation/RequestUserInputComposer'
import { ApprovalDecisionComposer } from '../conversation/ApprovalDecisionComposer'
import { ConversationWelcome } from '../conversation/ConversationWelcome'
import type { WorkspaceConfigChangedPayload } from '../../utils/workspaceConfigChanged'
import { useComposerModelControls } from '../conversation/useComposerModelControls'
import { AgentBuilderChatEmptyState } from '../agents/AgentBuilderChatEmptyState'
import { resolveComposerMascotEffectState } from '../conversation/composerMascotEffectState'
import {
  DesktopPluginConversationTabs,
  DesktopPluginConversationViewOutlet
} from '../desktopPlugins/DesktopPluginConversationView'
import { useDesktopPluginRegistry } from '../../plugins/desktopPluginRegistry'

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
  /** Explicit mascot character for the composer (e.g. the Agent Builder pane's edited-profile avatar). */
  mascotAvatar?: AvatarSpec
  /** Purpose-built embedded conversation surface for Agent Builder. */
  variant?: 'default' | 'agentBuilder'
  /** Called immediately before a user message is sent. */
  onBeforeSend?: () => Promise<void> | void
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
  minimalComposer = false,
  mascotAvatar,
  variant = 'default',
  onBeforeSend
}: ConversationPanelProps): JSX.Element {
  const isAgentBuilder = variant === 'agentBuilder'
  const [composerPrefillRequest, setComposerPrefillRequest] = useState<{ id: number; text: string } | null>(null)
  const activeThread = useThreadStore((s) => s.activeThread)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const loading = useThreadStore((s) => s.loading)
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const threadMode = useConversationStore((s) => s.threadMode)
  const pendingApproval = useConversationStore((s) => s.pendingApproval)
  const genericApproval = useConversationStore((s) => s.genericApproval)
  const conversationViews = useDesktopPluginRegistry((s) => s.conversationViews)
  const selectedConversationViewKey = useDesktopPluginRegistry((s) =>
    activeThreadId ? s.conversationSelections.get(activeThreadId) ?? null : null
  )
  // Tool approvals (turn-bound) take priority over turn-less approvals (e.g. browser-use).
  const composerApproval = pendingApproval ?? genericApproval
  const pendingUserInput = useConversationStore((s) => s.pendingUserInput)
  const latestCreatePlanTurnId = useConversationStore(selectLatestCreatePlanTurnId)
  const connectionStatus = useConnectionStore((s) => s.status)
  const connectionErrorMessage = useConnectionStore((s) => s.errorMessage)
  const planApprovalDismissed = useUIStore((s) => s.planApprovalDismissed)
  const resetPlanApprovalDismissed = useUIStore((s) => s.resetPlanApprovalDismissed)
  const protocolWorkspacePath = identityWorkspacePath || workspacePath
  const threadStateWorkspacePath = activeThread?.workspacePath || protocolWorkspacePath
  const activeEffectiveWorkspacePath =
    activeThread?.effectiveWorkspacePath?.trim() || threadStateWorkspacePath
  const modelControls = useComposerModelControls({
    workspacePath,
    remoteWorkspace,
    activeThread,
    activeThreadId,
    workspaceConfigChange,
    workspaceConfigChangeSeq
  })
  const mascotEffectState = resolveComposerMascotEffectState({
    modelName: modelControls.modelName,
    modelCatalog: modelControls.modelCatalog,
    reasoningValue: modelControls.reasoningValue,
    speedValue: modelControls.speedValue,
    contextMode: modelControls.contextMode,
    contextDegraded: modelControls.contextDegraded
  })

  const showReconnectionBanner = connectionStatus === 'disconnected'
  const showPlanApproval = !isAgentBuilder
    && threadMode === 'plan'
    && turnStatus === 'idle'
    && composerApproval == null
    && latestCreatePlanTurnId != null
    && planApprovalDismissed[latestCreatePlanTurnId] !== true

  useEffect(() => {
    resetPlanApprovalDismissed()
  }, [activeThreadId, resetPlanApprovalDismissed])

  // Loading state: thread selected but full data not yet fetched
  if (activeThreadId && !activeThread && (loading || isAgentBuilder)) {
    return (
      <div style={centeredStyle}>
        <span style={{ color: 'var(--text-dimmed)', fontSize: '13px' }}>Loading thread...</span>
      </div>
    )
  }

  // No thread selected — show the welcome card
  if (!activeThread) {
    if (isAgentBuilder) {
      return (
        <div style={centeredStyle}>
          <span style={{ color: 'var(--text-dimmed)', fontSize: '13px' }}>Starting builder...</span>
        </div>
      )
    }
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
  const selectedConversationView = !isAgentBuilder && selectedConversationViewKey
    ? conversationViews.find((view) => view.contributionKey === selectedConversationViewKey) ?? null
    : null

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

      {!isAgentBuilder && (
        <ThreadHeader
          threadName={threadName}
          threadId={activeThread.id}
          workspacePath={activeEffectiveWorkspacePath}
          remoteWorkspace={remoteWorkspace}
        />
      )}

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

      {!isAgentBuilder && <DesktopPluginConversationTabs threadId={activeThread.id} />}

      {/* Message stream (fills remaining space) */}
      {selectedConversationView ? (
        <DesktopPluginConversationViewOutlet
          contribution={selectedConversationView}
          threadId={activeThread.id}
        />
      ) : hasContent ? (
        <MessageStream />
      ) : isAgentBuilder ? (
        <AgentBuilderChatEmptyState
          onPick={(text) => {
            setComposerPrefillRequest({ id: Date.now(), text })
          }}
        />
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
          mascotEffectState={mascotEffectState}
        />
      ) : pendingUserInput ? (
        <RequestUserInputComposer
          request={pendingUserInput}
          onResponseAccepted={onInteractionResponseAccepted}
          mascotEffectState={mascotEffectState}
        />
      ) : showPlanApproval && latestCreatePlanTurnId ? (
        <PlanApprovalComposer
          threadId={activeThread.id}
          workspacePath={protocolWorkspacePath}
          turnId={latestCreatePlanTurnId}
          mascotEffectState={mascotEffectState}
        />
      ) : (
        <InputComposer
          threadId={activeThread.id}
          workspacePath={threadStateWorkspacePath}
          fileWorkspacePath={activeEffectiveWorkspacePath}
          remoteWorkspace={remoteWorkspace}
          minimalChrome={minimalComposer || isAgentBuilder}
          mascotAvatar={mascotAvatar}
          variant={variant}
          prefillRequest={composerPrefillRequest}
          onBeforeSend={onBeforeSend}
          modelName={modelControls.modelName}
          providerId={modelControls.providerId}
          providerOptions={modelControls.providerOptions}
          modelOptions={modelControls.modelOptions}
          modelCatalog={modelControls.modelCatalog}
          reasoningValue={modelControls.reasoningValue}
          speedValue={modelControls.speedValue}
          modelLoading={modelControls.modelLoading}
          modelDisabled={modelControls.modelDisabled}
          modelListUnsupportedEndpoint={modelControls.modelListUnsupportedEndpoint}
          modelCatalogError={modelControls.modelCatalogError}
          modelCatalogErrorMessage={modelControls.modelCatalogErrorMessage}
          onModelChange={modelControls.onModelChange}
          onProviderChange={modelControls.onProviderChange}
          onReasoningChange={modelControls.onReasoningChange}
          onSpeedChange={modelControls.onSpeedChange}
          onModelCatalogRetry={modelControls.onModelCatalogRetry}
          contextMode={modelControls.contextMode}
          contextSupportsMax={modelControls.contextSupportsMax}
          contextDegraded={modelControls.contextDegraded}
          contextConfiguredWindow={modelControls.contextConfiguredWindow}
          onContextModeChange={modelControls.onContextModeChange}
        />
      )}
    </div>
  )
}

const centeredStyle: CSSProperties = {
  display: 'flex',
  flex: 1,
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'transparent'
}
