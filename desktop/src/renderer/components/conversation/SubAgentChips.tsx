import { useMemo, useState, type CSSProperties, type JSX, type ReactNode } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { isSubAgentChildRunning, useSubAgentStore } from '../../stores/subAgentStore'
import type { ConversationItem } from '../../types/conversation'
import { ActionTooltip } from '../ui/ActionTooltip'
import { getSubAgentAccent, getSubAgentIdentitySeed } from '../../utils/subAgentPresentation'
import { avatarFromSeed } from '../agents/agentAvatar'
import { RobotAvatar } from '../agents/RobotAvatar'
import { resolveCoreToolRenderPlan } from '../../utils/toolRendererRegistry'
import { resolveDesktopPluginToolRenderer } from '../../plugins/desktopPluginRegistry'
import { isToolExecutionFailure } from '../../utils/toolCallDisplay'

const VISIBLE_CHIPS = 3

export interface SubAgentChipDisplay {
  id: string
  name: string
  prompt: string
  accentColor: string
  seed: string
  childThreadId: string | null
  pending: boolean
  failed: boolean
}

export function SubAgentChips({
  items,
  parentThreadId
}: {
  items: ConversationItem[]
  parentThreadId: string | null
}): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const [showAll, setShowAll] = useState(false)
  const children = useSubAgentStore((state) =>
    parentThreadId ? state.childrenByParent.get(parentThreadId) : undefined
  )

  const displays = useMemo(
    () => items.map(getSubAgentChipDisplay).filter((entry): entry is SubAgentChipDisplay => entry != null),
    [items]
  )

  if (displays.length === 0) return null

  const runningIds = new Set(
    (children ?? []).filter(isSubAgentChildRunning).map((child) => child.childThreadId)
  )
  // A spawn still in flight has no child id to match, so its own state counts as running.
  const anyRunning = displays.some((display) =>
    display.pending || (display.childThreadId != null && runningIds.has(display.childThreadId))
  )
  const anyFailed = displays.some((display) => display.failed)
  const visible = showAll ? displays : displays.slice(0, VISIBLE_CHIPS)
  const hidden = displays.length - visible.length

  return (
    <div className="dc-subagent-chips" data-testid="subagent-chips">
      <span className="dc-subagent-marks" aria-hidden>
        {visible.map((display) => (
          <span key={display.id} className="dc-subagent-mark">
            {/* Same identity the Subagents tab draws, so one agent reads the same in both places. */}
            <RobotAvatar spec={{ ...avatarFromSeed(display.seed), accessory: 0 }} size={16} />
          </span>
        ))}
      </span>
      {joinNames(locale, visible)}
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
        className={anyRunning && !anyFailed ? 'tool-running-gradient-text' : undefined}
        aria-live="polite"
      >
        {statusLabel(t, { anyFailed, anyRunning })}
      </span>
    </div>
  )
}

/** A failed spawn has no verb of its own, so it reads as interrupted. */
function statusLabel(
  t: (key: string) => string,
  state: { anyFailed: boolean; anyRunning: boolean }
): string {
  if (state.anyFailed) return t('subAgentChips.interrupted')
  if (state.anyRunning) return t('subAgentChips.startedWorking')
  return t('subAgentChips.finished')
}

/** Names read as one sentence, so the separators come from the locale rather than a hardcoded comma. */
function joinNames(locale: string, displays: SubAgentChipDisplay[]): ReactNode {
  const parts = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' })
    .formatToParts(displays.map((entry) => entry.name))
  let index = -1
  return parts.map((part, position) => {
    if (part.type !== 'element') return <span key={`sep-${position}`}>{part.value}</span>
    index += 1
    const display = displays[index]
    return <SubAgentName key={display.id} display={display} />
  })
}

function SubAgentName({ display }: { display: SubAgentChipDisplay }): JSX.Element {
  const t = useT()
  const label = t('subagentsPanel.openAria', { name: display.name })
  const tooltip = display.prompt ? `${display.name} — ${display.prompt}` : label

  const open = (): void => {
    if (display.childThreadId) {
      useThreadStore.getState().setActiveThreadId(display.childThreadId)
      useUIStore.getState().setActiveMainView('conversation')
      return
    }
    useUIStore.getState().setActiveDetailTab('subagents')
  }

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

  const parsed = parseJsonObject(item.result)
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
    pending: item.status !== 'completed',
    failed: isToolExecutionFailure(item)
  }
}

function truncatePrompt(value: string, maxChars: number): string {
  const trimmed = value.trim().replace(/\s+/g, ' ')
  const chars = Array.from(trimmed)
  if (chars.length <= maxChars) return trimmed
  return `${chars.slice(0, maxChars - 1).join('')}...`
}

function parseJsonObject(value: string | undefined): Record<string, unknown> | undefined {
  if (!value) return undefined
  try {
    const parsed = JSON.parse(value) as unknown
    if (typeof parsed === 'string') {
      const nested = JSON.parse(parsed) as unknown
      return typeof nested === 'object' && nested != null ? nested as Record<string, unknown> : undefined
    }
    return typeof parsed === 'object' && parsed != null ? parsed as Record<string, unknown> : undefined
  } catch {
    return undefined
  }
}

function getString(source: Record<string, unknown> | undefined, key: string): string | null {
  const value = source?.[key]
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}
