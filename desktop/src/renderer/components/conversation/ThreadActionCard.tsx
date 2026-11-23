import { memo, type CSSProperties } from 'react'
import { MessageSquarePlus, MessageSquareText } from 'lucide-react'
import { sameWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { useT } from '../../contexts/LocaleContext'
import { useLinkedChats } from '../../stores/linkedChatsStore'
import { useThreadStore } from '../../stores/threadStore'
import { threadActionProjectKey, type ThreadToolAction } from '../../utils/threadToolDisplay'
import { Button } from '../ui/Button'
import { RunningShimmer } from '../ui/RunningShimmer'
import { openThread } from '../sidebar/projectConnectionActions'
import { ThreadWaitingBadge } from '../sidebar/ThreadWaitingBadge'
import { useThreadActionTitle } from './useThreadActionTitle'

interface ThreadActionCardProps {
  action: ThreadToolAction
}

export const ThreadActionCard = memo(function ThreadActionCard({ action }: ThreadActionCardProps): JSX.Element {
  const t = useT()
  const projectKey = threadActionProjectKey(action)
  const parentThreadId = useThreadStore((s) => s.activeThreadId)
  const linked = useLinkedChats(parentThreadId).find((chat) =>
    chat.threadId === action.threadId && sameWorkspaceProjectKey(chat.projectId, projectKey)
  )
  const runningHere = useThreadStore((s) =>
    sameWorkspaceProjectKey(s.threadListProjectKey, projectKey) && s.runningTurnThreadIds.has(action.threadId)
  )
  const title = useThreadActionTitle(projectKey, action.threadId, linked?.title.trim() || action.displayName?.trim())

  const isCreated = action.kind === 'created'
  const working = (linked?.status ?? (runningHere ? 'running' : undefined)) === 'running'
  const waiting = !action.queued && linked?.status === 'waitingForInput'
    ? linked.waitingFor === 'approval' ? 'approval' : linked.waitingFor === 'planConfirmation' ? 'plan' : 'input'
    : null
  const outcome = t(action.queued ? 'threadActionCard.queuedSubtitle'
    : isCreated ? 'threadActionCard.createdTitle' : 'threadActionCard.messagedTitle')
  const name = title ?? (isCreated ? t('threadActionCard.untitled') : undefined)
  const subtitle = action.queued ? outcome : linked?.projectName ?? outcome

  function openChat(): void {
    void openThread(action.threadId, action.projectId ?? action.workspacePath)
  }

  return (
    <div style={cardStyle}>
      <button
        type="button"
        aria-label={name ? t('threadActionCard.openChatAria', { name }) : t('threadActionCard.openChat')}
        onClick={openChat}
        style={bodyButtonStyle}
      >
        <span style={iconStyle}>
          {isCreated
            ? <MessageSquarePlus size={22} strokeWidth={1.8} aria-hidden />
            : <MessageSquareText size={22} strokeWidth={1.8} aria-hidden />}
        </span>
        <span style={textWrapStyle}>
          {name || subtitle !== outcome ? (
            <>
              <span style={titleStyle}>{name ?? outcome}</span>
              <RunningShimmer active={working} style={subtitleStyle}>{subtitle}</RunningShimmer>
            </>
          ) : (
            <RunningShimmer active={working} style={titleStyle}>{outcome}</RunningShimmer>
          )}
        </span>
      </button>
      {waiting && <ThreadWaitingBadge status={waiting} />}
      <Button variant="outlineGhost" size="toolbar" onClick={openChat} style={{ flexShrink: 0 }}>
        {t('threadActionCard.openChat')}
      </Button>
    </div>
  )
})

const cardStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  padding: '0 14px 0 0',
  border: '1px solid var(--border-default)',
  borderRadius: '8px',
  background: 'var(--bg-primary)',
  boxShadow: '0 1px 2px rgba(0, 0, 0, 0.04)',
  overflow: 'hidden'
}

const bodyButtonStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  minHeight: '64px',
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  padding: '12px 0 12px 14px',
  border: 'none',
  background: 'transparent',
  color: 'inherit',
  cursor: 'pointer',
  textAlign: 'left',
  font: 'inherit'
}

const iconStyle: CSSProperties = {
  width: '44px',
  height: '44px',
  borderRadius: '10px',
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}

const textWrapStyle: CSSProperties = {
  minWidth: 0,
  flex: 1,
  display: 'block'
}

const titleStyle: CSSProperties = {
  display: 'block',
  fontSize: 'var(--conversation-font-size)',
  lineHeight: 'var(--conversation-line-height)',
  fontWeight: 600,
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const subtitleStyle: CSSProperties = {
  display: 'block',
  marginTop: '2px',
  fontSize: 'var(--conversation-secondary-size)',
  lineHeight: 'var(--conversation-line-height)',
  color: 'var(--text-secondary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}
