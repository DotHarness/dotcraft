import type { CSSProperties, KeyboardEvent, PointerEvent, ReactNode } from 'react'
import { Archive, ArrowRight, Coffee, Loader2, MessageSquare, Pencil, Trash2 } from 'lucide-react'
import { HISTORY_LAYOUTS } from './TeamsView.layout'
import { formatRelativeArchiveTime, isCancelledStatus, resolveHistoryOffsetY } from './TeamsView.model'
import type { BoardCard, CardOverride, DiscardAction, IntentKind, Mission } from './TeamsView.types'

const INTENT_ICONS: Record<IntentKind, ReactNode> = {
  plan: <Pencil size={12} strokeWidth={2.2} aria-hidden />,
  talk: <MessageSquare size={12} strokeWidth={2.2} aria-hidden />,
  task: <ArrowRight size={12} strokeWidth={2.4} aria-hidden />,
  work: <Loader2 size={12} strokeWidth={2.2} aria-hidden />,
  rest: <Coffee size={12} strokeWidth={2.2} aria-hidden />
}

const INTENT_LABELS: Record<IntentKind, string> = {
  plan: 'Plan',
  talk: 'Talk',
  task: 'To task',
  work: 'Working',
  rest: 'Rest'
}

type BoardCardStyle = CSSProperties & Record<`--${string}`, string | number>

export function BoardCardView({
  card,
  selected,
  dragging,
  hovering,
  elevated,
  override,
  refCallback,
  onPointerDown,
  onPointerMove,
  onPointerUp,
  onKeyDown,
  onMouseEnter,
  onMouseLeave
}: {
  card: BoardCard
  selected: boolean
  dragging: boolean
  hovering: boolean
  elevated: boolean
  override?: CardOverride
  refCallback: (element: HTMLDivElement | null) => void
  onPointerDown: (event: PointerEvent<HTMLDivElement>, card: BoardCard) => void
  onPointerMove: (event: PointerEvent<HTMLDivElement>, card: BoardCard) => void
  onPointerUp: (event: PointerEvent<HTMLDivElement>, card: BoardCard) => void
  onKeyDown: (event: KeyboardEvent<HTMLDivElement>, card: BoardCard) => void
  onMouseEnter: (card: BoardCard) => void
  onMouseLeave: (card: BoardCard) => void
}): JSX.Element {
  const left = `${override?.x ?? card.x}px`
  const top = `${override?.y ?? card.y}px`
  const rotation = override?.rotation ?? card.rotation
  const className = [
    'teams-table-card',
    `teams-card-${card.kind}`,
    selected ? 'selected' : '',
    dragging ? 'dragging' : '',
    hovering ? 'hovering' : '',
    elevated ? 'elevated' : '',
    card.completed ? 'completed' : '',
    card.spawned ? 'teams-card-spawned' : '',
    card.settling ? 'settling' : '',
    card.working ? 'working' : ''
  ].filter(Boolean).join(' ')
  const style: BoardCardStyle = {
    left,
    top,
    zIndex: elevated ? 900 : (override?.z ?? card.z),
    '--rot': `${rotation}deg`
  }
  if (card.spawned && card.spawnFlip) {
    const flip = card.spawnFlip
    style['--flip-from-x'] = `${flip.fromX}px`
    style['--flip-from-y'] = `${flip.fromY}px`
    if (flip.arcX !== undefined) style['--flip-arc-x'] = `${flip.arcX}px`
    if (flip.spinFrom !== undefined) style['--flip-spin-from'] = `${flip.spinFrom}deg`
    if (flip.spinMid !== undefined) style['--flip-spin-mid'] = `${flip.spinMid}deg`
  }

  return (
    <div
      ref={refCallback}
      className={className}
      data-card-key={card.key}
      data-card-kind={card.kind}
      data-role={card.roleKey}
      role="button"
      tabIndex={0}
      aria-label={card.title}
      style={style}
      onPointerDown={(event) => onPointerDown(event, card)}
      onPointerMove={(event) => onPointerMove(event, card)}
      onPointerUp={(event) => onPointerUp(event, card)}
      onKeyDown={(event) => onKeyDown(event, card)}
      onMouseEnter={() => onMouseEnter(card)}
      onMouseLeave={() => onMouseLeave(card)}
    >
      <div className="teams-card-face">
        {card.statusChip ? (
          <div className={`teams-card-status-chip ${card.statusChip.tone}`} aria-hidden="true">
            <span>{card.statusChip.label}</span>
          </div>
        ) : null}
        <div className="teams-card-strip">
          <span>{card.stripLabel}</span>
          <span>{card.stripMeta}</span>
        </div>
        {card.kind === 'member' && card.avatarSrc ? (
          <div className="teams-card-avatar">
            <img src={card.avatarSrc} alt="" draggable={false} />
          </div>
        ) : card.kind === 'draft' ? (
          <>
            <div className="teams-draft-title">{card.title}</div>
            <div className="teams-draft-prompt">{card.body}</div>
          </>
        ) : (
          <>
            <div className="teams-card-title">{card.title}</div>
            {card.note ? <div className="teams-card-note">{card.note}</div> : null}
          </>
        )}
        {typeof card.progress === 'number' && (
          <div
            className="teams-card-progress"
            style={{ '--progress': `${card.progress}%` } as CSSProperties}
            aria-hidden="true"
          />
        )}
        {card.completed && <div className="teams-card-checkmark" aria-hidden="true">✓</div>}
        {card.kind === 'member' && card.intent ? (
          <div className={`teams-intent-chip ${card.intent} show`} aria-hidden="true">
            {INTENT_ICONS[card.intent]}
            <span>{card.intentLabel ?? INTENT_LABELS[card.intent]}</span>
          </div>
        ) : null}
        {card.kind === 'member' && card.dialog ? (
          <div className="teams-dialog-bubble show" aria-hidden="true">{card.dialog}</div>
        ) : null}
      </div>
    </div>
  )
}

export function ArchivePile({
  count,
  archiveLabel,
  title,
  meta,
  selected,
  expanded,
  onClick
}: {
  count: number
  archiveLabel: string
  title: string
  meta: string
  selected: boolean
  expanded: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <button
      className={`teams-archive-pile ${selected ? 'selected' : ''}`}
      type="button"
      onClick={onClick}
      aria-expanded={expanded}
    >
      <span className="teams-archive-stack-layer" aria-hidden="true" />
      <span className="teams-archive-stack-layer" aria-hidden="true" />
      <span className="teams-archive-stack-layer" aria-hidden="true" />
      <span className="teams-archive-stack-top">
        <span className="teams-archive-stack-strip">
          <span>{archiveLabel}</span>
          <span aria-hidden="true">↴</span>
        </span>
        <span className="teams-archive-stack-title">{title}</span>
        <span className="teams-archive-stack-meta">{meta}</span>
      </span>
      <span className="teams-archive-pile-count">×{count}</span>
    </button>
  )
}

export function DiscardPile({
  refCallback,
  armed,
  over,
  busy,
  action,
  title,
  meta,
  busyLabel
}: {
  refCallback: (element: HTMLDivElement | null) => void
  armed: boolean
  over: boolean
  busy: boolean
  action?: DiscardAction
  title: string
  meta: string
  busyLabel: string
}): JSX.Element {
  return (
    <div
      ref={refCallback}
      className={`teams-discard-pile ${armed ? 'armed' : ''} ${over ? 'over' : ''} ${action === 'archive' ? 'archive-mode' : 'cancel-mode'}`}
      data-testid="teams-discard-pile"
      aria-hidden="true"
    >
      <span className="teams-discard-stack-layer" aria-hidden="true" />
      <span className="teams-discard-stack-layer" aria-hidden="true" />
      <span className="teams-discard-stack-top">
        <span className="teams-discard-stack-strip">
          {action === 'archive' ? <Archive size={11} strokeWidth={3} aria-hidden /> : <Trash2 size={11} strokeWidth={3} aria-hidden />}
          <span>{busy ? busyLabel : title}</span>
        </span>
        <span className="teams-discard-stack-title">{title}</span>
        <span className="teams-discard-stack-meta">{meta}</span>
      </span>
    </div>
  )
}

export function HistoryMissionCard({
  mission,
  index,
  boardVisibleLogicalHeight,
  archivedLabel,
  cancelledLabel,
  fallbackTitle,
  selected,
  leaving,
  onSelect
}: {
  mission: Mission
  index: number
  boardVisibleLogicalHeight: number
  archivedLabel: string
  cancelledLabel: string
  fallbackTitle: string
  selected: boolean
  leaving: boolean
  onSelect: () => void
}): JSX.Element {
  const layout = HISTORY_LAYOUTS[index] ?? HISTORY_LAYOUTS[0]
  const historyOffsetY = resolveHistoryOffsetY(boardVisibleLogicalHeight)
  const statusLabel = isCancelledStatus(mission.status) ? cancelledLabel : archivedLabel
  const className = [
    'teams-history-card',
    selected ? 'selected' : '',
    leaving ? 'leaving' : ''
  ].filter(Boolean).join(' ')
  const style = {
    '--x': `${layout.x}px`,
    '--y': `${layout.y + historyOffsetY}px`,
    '--rot': `${layout.rotation}deg`,
    '--deal-x': `${layout.dealX}px`,
    '--deal-y': `${layout.dealY}px`,
    '--delay': `${120 + index * 84}ms`,
    '--index': index
  } as CSSProperties

  return (
    <article
      className={className}
      style={style}
      role="button"
      tabIndex={0}
      aria-label={mission.title || fallbackTitle}
      onClick={onSelect}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          onSelect()
        }
      }}
    >
      <div className="teams-card-face">
        <div className="teams-card-strip">
          <span>{statusLabel}</span>
          <span>{isCancelledStatus(mission.status) ? '×' : '✓'}</span>
        </div>
        <div className="teams-history-title">{mission.title || fallbackTitle}</div>
        <div className="teams-history-meta">{formatRelativeArchiveTime(mission.archivedAt || mission.updatedAt)}</div>
      </div>
    </article>
  )
}

export function Metric({ value, label }: { value: string; label: string }): JSX.Element {
  return (
    <div className="teams-rail-stat">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  )
}
