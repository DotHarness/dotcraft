import { useMemo, useState, type CSSProperties, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { isSubAgentChildRunning, useSubAgentStore } from '../../stores/subAgentStore'
import type { ConversationItem } from '../../types/conversation'
import { ActionTooltip } from '../ui/ActionTooltip'
import { getSubAgentAccent, getSubAgentIdentitySeed } from '../../utils/subAgentPresentation'
import { resolveCoreToolRenderPlan } from '../../utils/toolRendererRegistry'
import { resolveDesktopPluginToolRenderer } from '../../plugins/desktopPluginRegistry'

const VISIBLE_CHIPS = 3

export interface SubAgentChipDisplay {
  id: string
  name: string
  prompt: string
  accentColor: string
  childThreadId: string | null
}

export function SubAgentChips({
  items,
  parentThreadId
}: {
  items: ConversationItem[]
  parentThreadId: string | null
}): JSX.Element | null {
  const t = useT()
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
  const anyRunning = displays.some(
    (display) => display.childThreadId != null && runningIds.has(display.childThreadId)
  )
  const visible = showAll ? displays : displays.slice(0, VISIBLE_CHIPS)
  const hidden = displays.length - visible.length

  return (
    <div className="dc-subagent-chips" data-testid="subagent-chips">
      {visible.map((display) => (
        <SubAgentChip
          key={display.id}
          display={display}
          running={display.childThreadId != null && runningIds.has(display.childThreadId)}
        />
      ))}
      {hidden > 0 && (
        <button
          type="button"
          className="dc-subagent-chips-more"
          onClick={() => setShowAll(true)}
        >
          {t('subAgentChips.more', { count: hidden })}
        </button>
      )}
      <span
        className={anyRunning ? 'tool-running-gradient-text' : undefined}
        aria-live="polite"
      >
        {anyRunning ? t('subAgentChips.startedWorking') : t('subAgentChips.finished')}
      </span>
    </div>
  )
}

function SubAgentChip({
  display,
  running
}: {
  display: SubAgentChipDisplay
  running: boolean
}): JSX.Element {
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
        className="dc-subagent-chip"
        data-running={running ? 'true' : 'false'}
        style={{ '--subagent-accent': display.accentColor } as CSSProperties}
        onClick={open}
        aria-label={label}
      >
        <span className="dc-subagent-chip-dot" aria-hidden />
        <span className="dc-subagent-chip-name">{display.name}</span>
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

  return {
    id: item.id,
    name,
    prompt: truncatePrompt(prompt, 180),
    accentColor: getSubAgentAccent(getSubAgentIdentitySeed({ agentPath, childThreadId, nickname: name })),
    childThreadId
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
