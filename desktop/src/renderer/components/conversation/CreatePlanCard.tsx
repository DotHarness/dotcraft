import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Check, ChevronDown, ChevronUp, Copy } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { addToast } from '../../stores/toastStore'
import { extractPartialTodos } from '../../stores/conversationStore'
import { extractPartialJsonStringValue } from '../../utils/toolCallDisplay'
import { parsePlanMarkdown } from '../../utils/planMarkdown'
import { MarkdownRenderer } from './MarkdownRenderer'
import { ActionTooltip } from '../ui/ActionTooltip'
import { PlanTodoStatusIcon } from '../plan/PlanTodoStatusIcon'

interface CreatePlanCardProps {
  item: ConversationItem
  locale: AppLocale
}

interface PlanTodo {
  id: string
  content: string
  status: 'pending' | 'in_progress' | 'completed' | 'cancelled'
}

interface ParsedCreatePlan {
  title: string
  overview: string
  content: string
  todos: PlanTodo[]
}

export function hasCreatePlanDisplayData(item: ConversationItem): boolean {
  const parsed = parseCreatePlanData(item)
  return (
    parsed.title.trim().length > 0
    || parsed.overview.trim().length > 0
    || parsed.content.trim().length > 0
    || parsed.todos.length > 0
  )
}

export function CreatePlanCard({ item, locale }: CreatePlanCardProps): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const [copied, setCopied] = useState(false)
  const copyResetTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const parsed = useMemo(() => parseCreatePlanData(item), [item])
  const isRunning = item.status !== 'completed'

  const title =
    parsed.title.trim().length > 0
      ? parsed.title
      : translate(locale, 'toolCall.plan.collapsedLabelFallback')
  const copyContent = useMemo(() => buildCopyContent(parsed, title), [parsed, title])
  const hasContent = parsed.content.trim().length > 0
  const hasTodos = parsed.todos.length > 0
  const canExpand = hasContent || hasTodos
  const badgeLabel = translate(
    locale,
    isRunning ? 'toolCall.plan.previewBadgeRunning' : 'toolCall.plan.previewBadge'
  )

  useEffect(() => {
    return () => {
      if (copyResetTimerRef.current != null) {
        clearTimeout(copyResetTimerRef.current)
        copyResetTimerRef.current = null
      }
    }
  }, [])

  useEffect(() => {
    if (copyResetTimerRef.current != null) {
      clearTimeout(copyResetTimerRef.current)
      copyResetTimerRef.current = null
    }
    setCopied(false)
  }, [item.id])

  async function handleCopy(): Promise<void> {
    if (!copyContent) return
    try {
      await navigator.clipboard.writeText(copyContent)
      setCopied(true)
      addToast(translate(locale, 'toast.copied'), 'success', 2000)
      if (copyResetTimerRef.current != null) {
        clearTimeout(copyResetTimerRef.current)
      }
      copyResetTimerRef.current = setTimeout(() => {
        setCopied(false)
        copyResetTimerRef.current = null
      }, 1500)
    } catch {
      // Ignore clipboard failures silently.
    }
  }

  const copyButton = copyContent ? (
    <IconButton
      icon={copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
      ariaLabel={translate(locale, copied ? 'toolCall.plan.copiedAria' : 'toolCall.plan.copyAria')}
      active={copied}
      onClick={() => {
        void handleCopy()
      }}
    />
  ) : null

  const expandButton = canExpand ? (
    <IconButton
      icon={expanded ? <ChevronUp size={14} aria-hidden /> : <ChevronDown size={14} aria-hidden />}
      ariaLabel={translate(locale, expanded ? 'toolCall.plan.collapseAria' : 'toolCall.plan.expandAria')}
      onClick={() => {
        setExpanded((v) => !v)
      }}
    />
  ) : null

  return (
    <div
      style={{
        minWidth: 0,
        maxWidth: '100%',
        boxSizing: 'border-box',
        border: '1px solid var(--border-default)',
        borderRadius: '8px',
        background: 'var(--bg-secondary)',
        padding: '10px',
        position: 'relative'
      }}
    >
      <div
        style={{
          position: 'absolute',
          top: '8px',
          right: '10px',
          display: 'flex',
          alignItems: 'center',
          gap: '6px'
        }}
      >
        {copyButton}
        {expandButton}
      </div>

      <div
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          padding: '2px 8px',
          borderRadius: '999px',
          background: 'var(--bg-tertiary)',
          color: 'var(--text-dimmed)',
          fontSize: '11px',
          marginBottom: '8px'
        }}
      >
        <span className={isRunning ? 'tool-running-gradient-text' : undefined}>
          {badgeLabel}
        </span>
      </div>

      <h3
        style={{
          margin: '0 0 8px',
          minWidth: 0,
          color: 'var(--text-primary)',
          fontSize: '18px',
          fontWeight: 700,
          overflowWrap: 'anywhere',
          wordBreak: 'break-word'
        }}
      >
        {title}
      </h3>

      {!hasContent && parsed.overview.trim().length > 0 && (
        <p
          style={{
            margin: '0 0 10px',
            minWidth: 0,
            color: 'var(--text-secondary)',
            whiteSpace: 'pre-wrap',
            lineHeight: 1.5,
            overflowWrap: 'anywhere',
            wordBreak: 'break-word'
          }}
        >
          {parsed.overview}
        </p>
      )}

      {hasContent && (
        <div style={{ position: 'relative', minWidth: 0, maxWidth: '100%', paddingBottom: expanded ? 0 : '28px' }}>
          <div style={planMarkdownFrameStyle(expanded)}>
            <MarkdownRenderer content={parsed.content} containOverflow />
          </div>
          {!expanded && (
            <button
              type="button"
              onClick={() => {
                setExpanded(true)
              }}
              style={{
                ...expandToggleStyle,
                position: 'absolute',
                left: '50%',
                transform: 'translateX(-50%)',
                bottom: 0
              }}
            >
              {translate(locale, 'toolCall.plan.expandButton')}
            </button>
          )}
        </div>
      )}

      {expanded && hasTodos && (
        <ul
          style={{
            margin: hasContent ? '12px 0 0' : 0,
            padding: 0,
            listStyle: 'none',
            display: 'grid',
            gap: '6px',
            color: 'var(--text-primary)',
            fontSize: '14px',
            lineHeight: 1.6
          }}
        >
          {parsed.todos.map((todo) => {
            const isCancelled = todo.status === 'cancelled'
            return (
              <li key={todo.id} style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                <PlanTodoStatusIcon status={todo.status} />
                <span
                  style={{
                    minWidth: 0,
                    textDecoration: isCancelled ? 'line-through' : 'none',
                    color: isCancelled ? 'var(--text-dimmed)' : 'var(--text-primary)',
                    overflowWrap: 'anywhere',
                    wordBreak: 'break-word'
                  }}
                >
                  {todo.content}
                </span>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

function buildCopyContent(parsed: ParsedCreatePlan, fallbackTitle: string): string {
  if (parsed.content.trim().length > 0) {
    return parsed.content
  }
  const blocks: string[] = []
  if (fallbackTitle.trim().length > 0) {
    blocks.push(`# ${fallbackTitle.trim()}`)
  }
  if (parsed.overview.trim().length > 0) {
    blocks.push(parsed.overview.trim())
  }
  if (parsed.todos.length > 0) {
    blocks.push(parsed.todos.map((todo) => `- ${todo.content}`).join('\n'))
  }
  return blocks.join('\n\n').trim()
}

function parseCreatePlanData(item: ConversationItem): ParsedCreatePlan {
  const plan = typeof item.arguments?.plan === 'string'
    ? item.arguments.plan
    : (extractPartialJsonStringValue(item.argumentsPreview ?? '', 'plan') ?? '')
  const markdown = parsePlanMarkdown(plan)
  const fallbackTitle = typeof item.arguments?.title === 'string'
    ? item.arguments.title
    : (extractPartialJsonStringValue(item.argumentsPreview ?? '', 'title') ?? '')
  const fallbackOverview = typeof item.arguments?.overview === 'string'
    ? item.arguments.overview
    : (extractPartialJsonStringValue(item.argumentsPreview ?? '', 'overview') ?? '')
  const fallbackContent = typeof item.arguments?.content === 'string' ? item.arguments.content : ''
  const title = markdown.title || fallbackTitle
  const overview = markdown.overview || fallbackOverview
  const content = markdown.content || fallbackContent
  // When the full arguments have parsed, use the structured todos. While the
  // CreatePlan call is still streaming, fall back to the same partial-todos
  // parser the Detail Panel uses so todos appear line by line as each object
  // closes, instead of all at once on completion.
  const todos = Array.isArray(item.arguments?.todos)
    ? normalizeTodoEntries(
      item.arguments.todos.filter(
        (entry): entry is Record<string, unknown> => typeof entry === 'object' && entry != null
      )
    )
    : normalizeTodoEntries(extractPartialTodos(item.argumentsPreview ?? ''))

  return { title, overview, content, todos }
}

function normalizeTodoEntries(
  entries: ReadonlyArray<{ id?: unknown; content?: unknown; status?: unknown }>
): PlanTodo[] {
  return entries
    .map((entry, index) => ({
      id: typeof entry.id === 'string' && entry.id.trim().length > 0 ? entry.id : `todo-${index}`,
      content: typeof entry.content === 'string' ? entry.content : '',
      status: normalizeTodoStatus(entry.status)
    }))
    .filter((todo) => todo.content.trim().length > 0)
}

function normalizeTodoStatus(value: unknown): PlanTodo['status'] {
  if (value === 'in_progress' || value === 'completed' || value === 'cancelled') {
    return value
  }
  return 'pending'
}

const expandToggleStyle: CSSProperties = {
  border: '1px solid var(--text-primary)',
  borderRadius: '999px',
  padding: '4px 10px',
  background: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  cursor: 'pointer',
  fontSize: '12px',
  fontWeight: 600
}

function planMarkdownFrameStyle(expanded: boolean): CSSProperties {
  const base: CSSProperties = {
    display: 'flow-root',
    minWidth: 0,
    maxWidth: '100%',
    boxSizing: 'border-box',
    overflowWrap: 'anywhere',
    wordBreak: 'break-word'
  }

  if (expanded) {
    return base
  }

  return {
    ...base,
    maxHeight: '220px',
    overflow: 'hidden',
    maskImage: 'linear-gradient(to bottom, #000 65%, transparent)',
    WebkitMaskImage: 'linear-gradient(to bottom, #000 65%, transparent)'
  }
}

function IconButton(
  {
    icon,
    ariaLabel,
    active = false,
    onClick
  }: {
    icon: JSX.Element
    ariaLabel: string
    active?: boolean
    onClick: () => void
  }
): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const chromeVisible = hovered || focused
  return (
    <ActionTooltip label={ariaLabel} placement="top">
      <button
        type="button"
        aria-label={ariaLabel}
        onClick={onClick}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        style={{
          width: '24px',
          height: '24px',
          borderRadius: '6px',
          border: '1px solid transparent',
          background: chromeVisible ? 'var(--bg-tertiary)' : 'transparent',
          color: active ? 'var(--success)' : chromeVisible ? 'var(--text-primary)' : 'var(--text-secondary)',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: 'pointer',
          transition: 'color 120ms ease, background 120ms ease'
        }}
      >
        {icon}
      </button>
    </ActionTooltip>
  )
}
