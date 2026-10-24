import { Fragment, useEffect, useMemo, useState, type JSX, type ReactNode } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { isSubAgentChildClosed, isTerminalSubAgentStatus, type SubAgentChild, type SubAgentDiscovery } from '../../stores/subAgentStore'
import { useSubAgentLookup } from '../../hooks/useSubAgentLookup'
import { findSubAgentChild } from '../../utils/subAgentIdentity'
import { useToolCallRevealTarget } from '../../stores/toolCallRevealStore'
import { openSubAgent } from '../../utils/subAgentNavigation'
import type { ConversationItem } from '../../types/conversation'
import { ActionTooltip } from '../ui/ActionTooltip'
import { RunningShimmer } from '../ui/RunningShimmer'
import { RobotAvatar } from '../agents/RobotAvatar'
import { resolveCoreToolRenderPlan } from '../../utils/toolRendererRegistry'
import { resolveDesktopPluginToolRenderer } from '../../plugins/desktopPluginRegistry'
import { isToolExecutionFailure, parseToolResultObject } from '../../utils/toolCallDisplay'

const VISIBLE_CHIPS = 3
const COLLAPSED_NAMES = 2
const NAMES_SLOT = '{{names}}'
const MORE_TOKEN = '__dotcraft_subagent_more__'

export interface SubAgentChipDisplay {
  id: string
  callId?: string
  name: string
  prompt: string
  childThreadId: string | null
  agentPath: string | null
  pending: boolean
  failed: boolean
  resultStatus: string | null
}

interface ResolvedSubAgentChip extends SubAgentChipDisplay {
  child: SubAgentChild | null
}

type AgentState = 'running' | 'done' | 'failed' | 'unknown'

export function SubAgentChips({
  items,
  parentThreadId,
  turnRunning
}: {
  items: ConversationItem[]
  parentThreadId: string
  turnRunning: boolean
}): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const [showAll, setShowAll] = useState(false)
  const parsedDisplays = useMemo(
    () => items.map(getSubAgentChipDisplay).filter((entry): entry is SubAgentChipDisplay => entry != null),
    [items]
  )
  const revealNonce = useToolCallRevealTarget((target) =>
    target.threadId === parentThreadId
      && parsedDisplays.length > VISIBLE_CHIPS
      && parsedDisplays.slice(COLLAPSED_NAMES).some((display) => display.callId === target.callId)
      ? target.nonce
      : null)
  useEffect(() => {
    if (revealNonce != null) setShowAll(true)
  }, [revealNonce])

  const { lookup, discovery } = useSubAgentLookup(parentThreadId, parsedDisplays.length > 0)
  const resolved = parsedDisplays.map((display) => {
    const child = findSubAgentChild(lookup, display.childThreadId, display.agentPath, 'children')
    const name = child?.nickname ?? display.name
    return { ...display, name, child }
  })
  if (resolved.length === 0) return null
  const states = resolved.map((display) => agentState(display, display.child, discovery, turnRunning))
  const anyFailed = states.includes('failed')
  const allDone = states.every((state) => state === 'done')
  const anyRunning = states.includes('running')
  const visible = !showAll && resolved.length > VISIBLE_CHIPS ? resolved.slice(0, COLLAPSED_NAMES) : resolved
  const hidden = resolved.length - visible.length
  const more = hidden > 0 && (
    <button
      type="button"
      className="dc-subagent-chips-more"
      onClick={() => setShowAll(true)}
    >
      {t('subAgentChips.more', { count: hidden })}
    </button>
  )
  const names = joinNames(locale, visible, more, (display) => {
    openSubAgent(parentThreadId, display.child?.childThreadId)
  })
  const statusKey = statusMessageKey({ anyFailed, allDone, anyRunning }, resolved.length)
  const [before = '', after = ''] = statusKey ? t(statusKey).split(NAMES_SLOT) : []
  const shimmer = !anyFailed && anyRunning

  return (
    <div className="dc-subagent-chips" data-testid="subagent-chips">
      <span className="dc-subagent-marks" aria-hidden>
        {visible.map((display) => (
          <span key={display.id} className="dc-subagent-mark">
            <RobotAvatar name={display.name} size={16} />
          </span>
        ))}
      </span>
      <span aria-live="polite">
        <StatusText text={before} active={shimmer} />
        {names}
        <StatusText text={after} active={shimmer} />
      </span>
    </div>
  )
}

/** The shimmer root is inline-block, which would swallow the space between a name and the status words. */
function StatusText({ text, active }: { text: string; active: boolean }): JSX.Element | null {
  const words = text.trim()
  if (!words) return text ? <>{text}</> : null
  const start = text.indexOf(words)
  return (
    <>
      {text.slice(0, start)}
      <RunningShimmer active={active} className="dc-subagent-chips-status">{words}</RunningShimmer>
      {text.slice(start + words.length)}
    </>
  )
}

function agentState(
  display: SubAgentChipDisplay,
  child: SubAgentChild | null,
  discovery: SubAgentDiscovery,
  turnRunning: boolean
): AgentState {
  if (display.failed) return 'failed'
  if (display.pending) return 'running'
  if (child) {
    if (isSubAgentChildClosed(child)) return 'done'
    if (child.runtime?.running === true) return 'running'
    if (isFailureStatus(child.status)) return 'failed'
    if (child.runtime?.running === false || child.isCompleted || isTerminalSubAgentStatus(child.status)) return 'done'
  }
  if (isFailureStatus(display.resultStatus)) return 'failed'
  if (isTerminalSubAgentStatus(display.resultStatus)) return 'done'
  if (turnRunning) return 'running'
  if (discovery.discovered) return 'done'
  return discovery.status === 'error' ? 'unknown' : 'running'
}

function isFailureStatus(status: string | null): boolean {
  return ['failed', 'cancelled', 'canceled', 'interrupted'].includes(status?.toLowerCase() ?? '')
}

/** A failed spawn has no verb of its own, so it reads as interrupted. */
function statusMessageKey(
  state: { anyFailed: boolean; allDone: boolean; anyRunning: boolean },
  count: number
): string | null {
  const form = count === 1 ? 'one' : 'other'
  if (state.anyFailed) return `subAgentChips.interrupted.${form}`
  if (state.anyRunning) return `subAgentChips.startedWorking.${form}`
  return state.allDone ? `subAgentChips.finished.${form}` : null
}

/** Names read as one sentence, so the separators come from the locale rather than a hardcoded comma. */
function joinNames(
  locale: string,
  displays: ResolvedSubAgentChip[],
  more: ReactNode,
  open: (display: ResolvedSubAgentChip) => void
): ReactNode {
  const parts = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' })
    .formatToParts([...displays.map((entry) => entry.name), ...(more ? [MORE_TOKEN] : [])])
  let index = -1
  return parts.map((part, position) => {
    if (part.type !== 'element') return <span key={`sep-${position}`}>{part.value}</span>
    if (part.value === MORE_TOKEN) return <Fragment key="more">{more}</Fragment>
    index += 1
    const display = displays[index]
    return <SubAgentName key={display.id} display={display} open={() => open(display)} />
  })
}

function SubAgentName({ display, open }: { display: SubAgentChipDisplay; open: () => void }): JSX.Element {
  const t = useT()
  const label = t('subagentsPanel.openAria', { name: display.name })
  const tooltip = display.prompt ? `${display.name} — ${display.prompt}` : label

  return (
    <ActionTooltip label={tooltip} placement="top">
      <button
        type="button"
        className="dc-subagent-name"
        data-call-id={display.callId}
        onClick={open}
        aria-label={label}
      >
        {display.name}
      </button>
    </ActionTooltip>
  )
}

export function getSubAgentChipDisplay(item: ConversationItem): SubAgentChipDisplay | null {
  const presentationId = item.presentation?.presentationId
  const plan = presentationId && resolveDesktopPluginToolRenderer(presentationId)
    ? null
    : resolveCoreToolRenderPlan(item)
  if (plan?.family !== 'subagent' || plan.options.operation !== 'spawn') return null

  const parsed = parseToolResultObject(item.result)
  const args = item.arguments
  const agentPath = getString(parsed, 'agentPath') ?? getString(args, 'target')
  const childThreadId = getString(parsed, 'childThreadId')
    ?? getString(parsed, 'agentId')
    ?? getString(args, 'childThreadId')
    ?? getString(args, 'agentId')
  const name = getString(parsed, 'agentNickname')
    ?? getString(parsed, 'nickname')
    ?? getString(parsed, 'taskName')
    ?? getString(args, 'agentNickname')
    ?? getString(args, 'nickname')
    ?? getString(args, 'taskName')
  if (!name) return null

  const prompt = getString(args, 'message')
    ?? getString(args, 'agentPrompt')
    ?? getString(args, 'prompt')
    ?? ''

  return {
    id: item.id,
    callId: item.toolCallId,
    name,
    prompt: truncatePrompt(prompt, 180),
    childThreadId,
    agentPath,
    pending: item.status !== 'completed' || item.result == null,
    failed: isToolExecutionFailure(item),
    resultStatus: getString(parsed, 'status')
  }
}

function truncatePrompt(value: string, maxChars: number): string {
  const trimmed = value.trim().replace(/\s+/g, ' ')
  const chars = Array.from(trimmed)
  if (chars.length <= maxChars) return trimmed
  return `${chars.slice(0, maxChars - 1).join('')}...`
}

function getString(source: Record<string, unknown> | undefined, key: string): string | null {
  const value = source?.[key]
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}
