import { useMemo, useState, type CSSProperties, type JSX, type ReactNode } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { isSubAgentChildClosed, isTerminalSubAgentStatus, type SubAgentChild, type SubAgentDiscovery } from '../../stores/subAgentStore'
import { useSubAgentLookup } from '../../hooks/useSubAgentLookup'
import { findSubAgentChild, type SubAgentScope } from '../../utils/subAgentIdentity'
import { openSubAgent } from '../../utils/subAgentNavigation'
import type { ConversationItem } from '../../types/conversation'
import { ActionTooltip } from '../ui/ActionTooltip'
import {
  getSubAgentAccent,
  getSubAgentIdentitySeed
} from '../../utils/subAgentPresentation'
import { avatarFromSeed } from '../agents/agentAvatar'
import { RobotAvatar } from '../agents/RobotAvatar'
import { resolveCoreToolRenderPlan } from '../../utils/toolRendererRegistry'
import { resolveDesktopPluginToolRenderer } from '../../plugins/desktopPluginRegistry'
import { isToolExecutionFailure, parseToolResultObject } from '../../utils/toolCallDisplay'

const VISIBLE_CHIPS = 3

export interface SubAgentChipDisplay {
  id: string
  name: string
  prompt: string
  accentColor: string
  seed: string
  childThreadId: string | null
  agentPath: string | null
  pending: boolean
  failed: boolean
  scope: SubAgentScope
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

  const { lookup, discovery } = useSubAgentLookup(parentThreadId, parsedDisplays.length > 0)
  const resolved = parsedDisplays.map((display) => {
    const child = findSubAgentChild(lookup, display.childThreadId, display.agentPath, display.scope)
    const name = child?.nickname ?? display.name
    const seed = getSubAgentIdentitySeed(child ?? display) ?? display.seed
    return { ...display, name, seed, accentColor: getSubAgentAccent(seed), child }
  })
  if (resolved.length === 0) return null
  const states = resolved.map((display) => agentState(display, display.child, discovery, turnRunning))
  const anyFailed = states.includes('failed')
  const allDone = states.every((state) => state === 'done')
  const anyRunning = states.includes('running')
  const visible = showAll ? resolved : resolved.slice(0, VISIBLE_CHIPS)
  const hidden = resolved.length - visible.length

  return (
    <div className="dc-subagent-chips" data-testid="subagent-chips">
      <span className="dc-subagent-marks" aria-hidden>
        {visible.map((display) => (
          <span key={display.id} className="dc-subagent-mark">
            <RobotAvatar spec={{ ...avatarFromSeed(display.seed), accessory: 0 }} size={16} />
          </span>
        ))}
      </span>
      {joinNames(locale, visible, (display) => {
        openSubAgent(parentThreadId, display.child)
      })}
      {hidden > 0 && (
        <>
          {' '}
          <button
            type="button"
            className="dc-subagent-chips-more"
            onClick={() => setShowAll(true)}
          >
            {t('subAgentChips.more', { count: hidden })}
          </button>
        </>
      )}
      {' '}
      <span
        className={!anyFailed && anyRunning ? 'tool-running-gradient-text' : undefined}
        aria-live="polite"
      >
        {statusLabel(t, { anyFailed, allDone, anyRunning })}
      </span>
    </div>
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
function statusLabel(
  t: (key: string) => string,
  state: { anyFailed: boolean; allDone: boolean; anyRunning: boolean }
): string {
  if (state.anyFailed) return t('subAgentChips.interrupted')
  if (state.anyRunning) return t('subAgentChips.startedWorking')
  return state.allDone ? t('subAgentChips.finished') : ''
}

/** Names read as one sentence, so the separators come from the locale rather than a hardcoded comma. */
function joinNames(locale: string, displays: ResolvedSubAgentChip[], open: (display: ResolvedSubAgentChip) => void): ReactNode {
  const parts = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' })
    .formatToParts(displays.map((entry) => entry.name))
  let index = -1
  return parts.map((part, position) => {
    if (part.type !== 'element') return <span key={`sep-${position}`}>{part.value}</span>
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
        style={{ '--subagent-accent': display.accentColor } as CSSProperties}
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
  const operation = plan?.options.operation
  if (plan?.family !== 'subagent' || (operation !== 'spawn' && operation !== 'followupTask')) return null

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

  const seed = getSubAgentIdentitySeed({ agentPath, childThreadId, nickname: name }) ?? name

  return {
    id: item.id,
    name,
    prompt: truncatePrompt(prompt, 180),
    accentColor: getSubAgentAccent(seed),
    seed,
    childThreadId,
    agentPath,
    pending: item.status !== 'completed' || item.result == null,
    failed: isToolExecutionFailure(item),
    scope: operation === 'spawn' ? 'children' : 'tree',
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
