import { useState, type CSSProperties } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { SubAgentChild } from '../../stores/subAgentStore'
import { findSubAgentChild, type SubAgentLookupSources } from '../../utils/subAgentIdentity'
import { formatSubAgentMeta, getSubAgentAccent, getSubAgentIdentitySeed } from '../../utils/subAgentPresentation'
import { parseToolResultObject } from '../../utils/toolCallDisplay'
import { openSubAgent } from '../../utils/subAgentNavigation'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ToolDisclosure } from './ToolDisclosure'

interface SubAgentToolDisplay {
  titleKey: string
  name: string
  meta: string
  prompt: string | null
  accentColor: string
  child: SubAgentChild | null
  message: string | null
  failed: boolean
}

export function SubAgentToolResultCard({
  display,
  locale,
  sourceThreadId
}: {
  sourceThreadId: string
  display: SubAgentToolDisplay
  locale: AppLocale
}): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const hasMessage = !!display.message

  const title = (
    <span style={subAgentResultContentStyle}>
      <span>
        <button type="button" className="dc-subagent-name"
          onClick={(event) => { event.stopPropagation(); openSubAgent(sourceThreadId, display.child) }}>
          {renderSubAgentTitle(locale, display.titleKey, display.name, display.accentColor)}
        </button>
        {display.meta && <span style={subAgentMetaStyle}>({display.meta})</span>}
      </span>
      {display.prompt && (
        <ActionTooltip label={display.prompt} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden' }}>
          <span style={{ ...subAgentPromptStyle, display: 'block' }}>
            {translate(locale, 'toolCall.subAgent.prompt', { prompt: display.prompt })}
          </span>
        </ActionTooltip>
      )}
    </span>
  )

  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((v) => !v)}
      expandable={hasMessage}
      tone={display.failed ? 'error' : undefined}
      title={title}
    >
      <div
        className="selectable dc-tool-panel-surface"
        data-padded="true"
        style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}
      >
        {display.message}
      </div>
    </ToolDisclosure>
  )
}

const subAgentResultContentStyle: CSSProperties = {
  display: 'inline-flex',
  flexDirection: 'column',
  gap: '2px',
  minWidth: 0,
  maxWidth: '100%'
}

const subAgentMetaStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  marginLeft: 6
}

const subAgentPromptStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const SUB_AGENT_NAME_TOKEN = '__DOTCRAFT_SUB_AGENT_NAME__'

function renderSubAgentTitle(
  locale: AppLocale,
  titleKey: string,
  name: string,
  accentColor: string
): JSX.Element {
  const template = translate(locale, titleKey, { name: SUB_AGENT_NAME_TOKEN })
  const parts = template.split(SUB_AGENT_NAME_TOKEN)
  if (parts.length === 1) {
    return <span>{translate(locale, titleKey, { name })}</span>
  }

  return (
    <span>
      {parts.map((part, index) => (
        <span key={`${part}-${index}`}>
          {part}
          {index < parts.length - 1 && (
            <span style={{ color: accentColor, fontWeight: 600 }}>{name}</span>
          )}
        </span>
      ))}
    </span>
  )
}

export function getSubAgentToolDisplay(
  operation: unknown,
  args: Record<string, unknown> | undefined,
  result: string | undefined,
  success: boolean,
  locale: AppLocale,
  lookup: SubAgentLookupSources
): SubAgentToolDisplay | null {
  if (!isSubAgentOperation(operation)) return null
  if (operation === 'wait' && result === undefined) return null
  const parsed = parseToolResultObject(result)
  const profile = getString(parsed, 'profileName') ?? getString(args, 'profile')
  const runtimeType = getString(parsed, 'runtimeType')
  const agentRole = getString(parsed, 'agentRole') ?? getString(args, 'agentRole')
  const agentPath = getString(parsed, 'agentPath')
    ?? getString(args, 'target')
  const explicitChildThreadId = getString(parsed, 'childThreadId')
    ?? getString(parsed, 'agentId')
    ?? getString(args, 'agentId')
    ?? getString(args, 'childThreadId')
  const matchedChild = findSubAgentChild(lookup, explicitChildThreadId, agentPath, operation === 'spawn' ? 'children' : 'tree')
  const childThreadId = matchedChild?.childThreadId ?? explicitChildThreadId ?? null
  const resolvedAgentPath = matchedChild?.agentPath ?? agentPath ?? null
  const status = getString(parsed, 'status')?.toLowerCase()
  const error = getString(parsed, 'error') ?? getString(parsed, 'message')
  const message = operation === 'wait'
    ? getString(parsed, 'message') ?? getString(parsed, 'result')
    : null
  const label = resolveSubAgentDisplayName(parsed, args, childThreadId, resolvedAgentPath, locale, matchedChild)
  const prompt = operation === 'spawn'
    ? getString(args, 'message') ?? getString(args, 'agentPrompt')
    : null
  const isTimeout = operation === 'wait'
    && (status === 'timeout' || isTimeoutMessage(error) || isTimeoutMessage(message))
  const failed = !isTimeout && (!success || status === 'failed')
  const titleKey = isTimeout
    ? 'toolCall.subAgent.timeout'
    : !success || status === 'failed'
      ? 'toolCall.subAgent.failed'
      : getSubAgentCompletedTitleKey(operation)
  return {
    titleKey,
    name: label,
    meta: formatSubAgentMeta({ agentRole, profileName: profile, runtimeType }),
    prompt: prompt ? truncateSubAgentPrompt(prompt, 120) : null,
    accentColor: getSubAgentAccent(getSubAgentIdentitySeed({
      agentPath: resolvedAgentPath,
      childThreadId,
      nickname: label
    })),
    child: matchedChild,
    message: isTimeout
      ? (message && !isTimeoutMessage(message) ? message : null)
      : !success && error
        ? error
        : message,
    failed
  }
}

function truncateSubAgentPrompt(value: string, maxChars: number): string {
  const trimmed = value.trim().replace(/\s+/g, ' ')
  const chars = Array.from(trimmed)
  if (chars.length <= maxChars) return trimmed
  return `${chars.slice(0, maxChars - 1).join('')}…`
}

export function formatSubAgentRunningLabel(
  operation: unknown,
  args: Record<string, unknown> | undefined,
  locale: AppLocale,
  lookup: SubAgentLookupSources
): string | null {
  if (!isSubAgentOperation(operation)) return null
  const explicitChildThreadId = getString(args, 'childThreadId') ?? getString(args, 'agentId')
  const agentPath = getString(args, 'target')
  const matchedChild = findSubAgentChild(lookup, explicitChildThreadId, agentPath, operation === 'spawn' ? 'children' : 'tree')
  const childThreadId = matchedChild?.childThreadId ?? explicitChildThreadId ?? null
  const resolvedAgentPath = matchedChild?.agentPath ?? agentPath ?? null
  const label = resolveSubAgentDisplayName(undefined, args, childThreadId, resolvedAgentPath, locale, matchedChild)
  const key = operation === 'spawn'
    ? 'toolCall.subAgent.starting'
    : operation === 'wait'
      ? 'toolCall.subAgent.waiting'
      : getSubAgentRunningTitleKey(operation)
  return translate(locale, key, { name: label })
}

function getSubAgentCompletedTitleKey(operation: SubAgentOperation): string {
  if (operation === 'spawn') return 'toolCall.subAgent.spawned'
  if (operation === 'wait') return 'toolCall.subAgent.waited'
  if (operation === 'sendMessage') return 'toolCall.subAgent.sentMessage'
  if (operation === 'followupTask') return 'toolCall.subAgent.followedUp'
  if (operation === 'list') return 'toolCall.subAgent.listed'
  if (operation === 'sendInput') return 'toolCall.subAgent.sentInput'
  if (operation === 'resume') return 'toolCall.subAgent.resumed'
  return 'toolCall.subAgent.closed'
}

function getSubAgentRunningTitleKey(operation: SubAgentOperation): string {
  if (operation === 'sendMessage') return 'toolCall.subAgent.sendingMessage'
  if (operation === 'followupTask') return 'toolCall.subAgent.followingUp'
  if (operation === 'list') return 'toolCall.subAgent.listing'
  if (operation === 'sendInput') return 'toolCall.subAgent.sendingInput'
  if (operation === 'resume') return 'toolCall.subAgent.resuming'
  return 'toolCall.subAgent.closing'
}

function resolveSubAgentDisplayName(
  parsed: Record<string, unknown> | undefined,
  args: Record<string, unknown> | undefined,
  childThreadId: string | null | undefined,
  agentPath: string | null | undefined,
  locale: AppLocale,
  matchedChild: SubAgentChild | null
): string {
  const explicitDisplayName = getString(parsed, 'displayName') ?? getString(args, 'displayName')
  if (explicitDisplayName && !isThreadIdLike(explicitDisplayName, childThreadId)) return explicitDisplayName

  if (matchedChild?.nickname && !isThreadIdLike(matchedChild.nickname, childThreadId)) {
    return matchedChild.nickname
  }

  const explicitName = getString(parsed, 'agentNickname')
    ?? getString(parsed, 'nickname')
    ?? getString(args, 'agentNickname')
    ?? getString(args, 'nickname')
    ?? getString(parsed, 'taskName')
    ?? getString(args, 'taskName')
    ?? getAgentPathSegment(agentPath ?? childThreadId)
  if (explicitName && !isThreadIdLike(explicitName, childThreadId)) return explicitName

  return translate(locale, 'toolCall.subAgent.agent')
}

function getAgentPathSegment(value: string | null | undefined): string | null {
  if (!value?.startsWith('/root/')) return null
  const parts = value.split('/').filter((part) => part.length > 0)
  return parts.length > 0 ? parts[parts.length - 1] : null
}

function isThreadIdLike(value: string, childThreadId: string | null | undefined): boolean {
  const normalized = value.trim()
  return normalized.length === 0
    || normalized === childThreadId
    || /^thread[_-]/i.test(normalized)
}

function isTimeoutMessage(value: string | null): boolean {
  if (!value) return false
  const normalized = value.toLowerCase()
  return normalized.includes('timed out') || normalized.includes('timeout')
}

type SubAgentOperation = 'spawn' | 'wait' | 'sendInput' | 'sendMessage' | 'followupTask' | 'resume' | 'list' | 'close'

function isSubAgentOperation(operation: unknown): operation is SubAgentOperation {
  return operation === 'spawn'
    || operation === 'wait'
    || operation === 'sendInput'
    || operation === 'sendMessage'
    || operation === 'followupTask'
    || operation === 'resume'
    || operation === 'list'
    || operation === 'close'
}

function getString(source: Record<string, unknown> | undefined, key: string): string | null {
  const value = source?.[key]
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}
