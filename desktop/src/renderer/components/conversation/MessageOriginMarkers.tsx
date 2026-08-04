import { Bot, CornerDownRight, MessagesSquare, Target, UsersRound } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { useCronStore } from '../../stores/cronStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import type { AppLocale } from '../../../shared/locales/types'
import type { ConversationItem } from '../../types/conversation'
import { ActionTooltip } from '../ui/ActionTooltip'

type TriggerKind = NonNullable<ConversationItem['triggerKind']>

/**
 * Markers that sit outside a user bubble.
 *
 * Origin goes above the bubble and special state goes into the message action
 * row below it, so the bubble itself holds only what the person wrote. Spec
 * §10.3.2 and specs/architecture/DESIGN.md → Message Markers.
 */

function isSubAgentKind(kind: TriggerKind): boolean {
  return kind === 'subagentFollowupTask' || kind === 'subagentMailbox' || kind === 'subagentInput'
}

function badgeTextFor(locale: AppLocale, kind: TriggerKind): string {
  if (kind === 'goal') return translate(locale, 'goal.triggeredBy.badge')
  if (kind === 'team') return translate(locale, 'teams.triggeredBy.badge')
  if (kind === 'app') return translate(locale, 'app.triggeredBy.badge')
  if (kind === 'thread') return translate(locale, 'thread.triggeredBy.badge')
  if (isSubAgentKind(kind)) return translate(locale, 'subAgent.triggeredBy.badge')
  return translate(locale, 'automation.triggeredBy.badge')
}

function detailTextFor(locale: AppLocale, kind: TriggerKind, label?: string): string {
  if (kind === 'goal') return label || translate(locale, 'goal.triggeredBy.generic')
  if (kind === 'team') {
    return label
      ? translate(locale, 'teams.triggeredBy.detail', { label })
      : translate(locale, 'teams.triggeredBy.generic')
  }
  if (kind === 'app') {
    return label
      ? translate(locale, 'app.triggeredBy.detail', { label })
      : translate(locale, 'app.triggeredBy.generic')
  }
  if (isSubAgentKind(kind)) {
    if (!label) return translate(locale, 'subAgent.triggeredBy.generic')
    return translate(
      locale,
      kind === 'subagentFollowupTask'
        ? 'subAgent.triggeredBy.followup'
        : kind === 'subagentMailbox'
          ? 'subAgent.triggeredBy.mailbox'
          : 'subAgent.triggeredBy.input',
      { label }
    )
  }
  if (kind === 'thread') {
    return label
      ? translate(locale, 'thread.triggeredBy.detail', { label })
      : translate(locale, 'thread.triggeredBy.generic')
  }
  if (!label) return translate(locale, 'automation.triggeredBy.generic')
  return translate(
    locale,
    kind === 'heartbeat'
      ? 'automation.triggeredBy.heartbeat'
      : kind === 'cron'
        ? 'automation.triggeredBy.cron'
        : 'automation.triggeredBy.task',
    { label }
  )
}

function OriginIcon({ kind }: { kind: TriggerKind }): JSX.Element {
  if (kind === 'goal') return <Target size={13} strokeWidth={1.8} aria-hidden />
  if (kind === 'team') return <UsersRound size={13} strokeWidth={1.8} aria-hidden />
  if (kind === 'thread' || isSubAgentKind(kind)) {
    return <MessagesSquare size={13} strokeWidth={1.8} aria-hidden />
  }
  return <Bot size={13} strokeWidth={1.8} aria-hidden />
}

/** Right-aligned note above the bubble naming where the turn came from. */
export function MessageOriginLine({
  kind,
  label,
  refId
}: {
  kind: TriggerKind
  label?: string
  refId?: string
}): JSX.Element {
  const locale = useLocale()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setAutomationsTab = useUIStore((s) => s.setAutomationsTab)
  const selectCronJob = useCronStore((s) => s.selectCronJob)

  const badgeText = badgeTextFor(locale, kind)
  const detailText = detailTextFor(locale, kind, label)
  // Screen readers are not subject to the tooltip's single-line clamp, so the
  // accessible name keeps the whole sentence.
  const title = `${badgeText} · ${detailText}`
  // The tooltip carries only what the line does not already say. Repeating the
  // badge would push the originating thread or job name past the clamp, and
  // without a label the detail is just a generic restatement of the badge.
  const hint = label ? detailText : null

  // Teams is an extension main view (`extension:agent-teams:...`), so a team
  // origin has no stable built-in route to offer. It stays inert rather than
  // presenting a target that goes nowhere.
  const canNavigate = (kind === 'cron' || kind === 'automation' || kind === 'thread') && !!refId

  const onClick = canNavigate
    ? () => {
        if (kind === 'thread') {
          if (refId) {
            useThreadStore.getState().setActiveThreadId(refId)
            setActiveMainView('conversation')
          }
          return
        }
        setActiveMainView('automations')
        if (kind === 'cron') {
          setAutomationsTab('cron')
          if (refId) selectCronJob(refId)
        } else if (kind === 'automation') {
          setAutomationsTab('tasks')
        }
      }
    : undefined

  const content = (
    <>
      <OriginIcon kind={kind} />
      <span>{badgeText}</span>
    </>
  )

  const line = onClick ? (
    <button
      type="button"
      className="dc-quiet-action dc-message-origin"
      onClick={onClick}
      aria-label={title}
    >
      {content}
    </button>
  ) : (
    <span className="dc-message-origin">{content}</span>
  )

  if (!hint) return line

  return (
    <ActionTooltip label={hint} placement="top" wrapperStyle={{ display: 'inline-flex' }}>
      {line}
    </ActionTooltip>
  )
}

/** Marks a message that steered a turn already in flight. */
export function SteeredOriginLine(): JSX.Element {
  const t = useT()

  return (
    <span className="dc-message-origin">
      <CornerDownRight size={13} strokeWidth={1.8} aria-hidden />
      <span>{t('conversation.steeredConversation')}</span>
    </span>
  )
}

/** Persistent note in the message action row for a user-authored goal. */
export function SentAsGoalMarker(): JSX.Element {
  const locale = useLocale()

  return (
    <span className="dc-message-state">
      <Target size={12} strokeWidth={1.9} aria-hidden />
      <span>{translate(locale, 'goal.sentAsGoal.badge')}</span>
    </span>
  )
}
