import { useMemo, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import {
  buildStreamingPlanDraft,
  selectStreamingPlanItemId,
  selectStreamingPlanRawArgs,
  useConversationStore
} from '../../stores/conversationStore'
import type { PlanTodoItem, PlanTodoStatus } from '../../stores/conversationStore'
import { PlanTodoStatusIcon } from '../plan/PlanTodoStatusIcon'
import { Skeleton } from '../ui/Skeleton'

/**
 * Plan tab — renders the agent's plan from plan/updated events.
 * While a `CreatePlan` tool call is streaming, the draft is rendered live
 * so the user can see the plan forming in real time.
 * Shows title, overview, and todo list with status icons.
 * Spec §11.4
 */
export function PlanTab(): JSX.Element {
  const t = useT()
  const plan = useConversationStore((s) => s.plan)
  const streamingItemId = useConversationStore(selectStreamingPlanItemId)
  const streamingRawArgs = useConversationStore(selectStreamingPlanRawArgs)
  const streamingDraft = useMemo(
    () => (streamingItemId ? buildStreamingPlanDraft(streamingItemId, streamingRawArgs ?? '') : null),
    [streamingItemId, streamingRawArgs]
  )
  const streamingTodos = useMemo(
    () => normalizeStreamingTodos(streamingDraft?.todos ?? []),
    [streamingDraft?.todos]
  )

  if (streamingItemId) {
    if (streamingDraft && (streamingDraft.overview || streamingTodos.length > 0)) {
      return (
        <div style={planScrollContainerStyle} aria-busy="true">
          {streamingDraft.title && (
            <h2
              style={{
                ...planTextContainmentStyle,
                margin: '0 0 4px',
                fontSize: '14px',
                fontWeight: 600,
                color: 'var(--text-primary)'
              }}
            >
              {streamingDraft.title}
            </h2>
          )}
          {streamingDraft.title && (
            <hr
              style={{
                border: 'none',
                borderTop: '1px solid var(--border-default)',
                margin: '8px 0'
              }}
            />
          )}
          {streamingDraft.overview && (
            <p
              style={{
                ...planTextContainmentStyle,
                margin: '0 0 12px',
                fontSize: '13px',
                color: 'var(--text-secondary)',
                lineHeight: 1.6
              }}
            >
              {streamingDraft.overview}
            </p>
          )}
          {streamingTodos.length > 0 && (
            <PlanTodoList todos={streamingTodos} />
          )}
          {/* Trailing skeleton rows mark the todos still streaming in — no
              spinner and no "loading" label; the skeleton pulse is the cue. */}
          <PlanDraftTodoSkeleton
            count={2}
            style={{ marginTop: streamingTodos.length > 0 ? '6px' : '0' }}
          />
        </div>
      )
    }

    // Nothing has arrived yet: the plan's shape is known, so render a full
    // shape-matched skeleton (title bar + overview lines + todo rows) instead of
    // a centered spinner. The pulse is the running signal.
    return <PlanDraftSkeleton label={t('plan.streamingDraftBadge')} />
  }

  if (!plan) {
    return (
      <div
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '16px'
        }}
      >
        <p
          style={{
            textAlign: 'center',
            color: 'var(--text-dimmed)',
            fontSize: '13px',
            lineHeight: 1.7,
            whiteSpace: 'pre-line'
          }}
        >
          {t('plan.empty')}
        </p>
      </div>
    )
  }

  return (
    <div style={planScrollContainerStyle}>
      {plan.title && (
        <h2
          style={{
            ...planTextContainmentStyle,
            margin: '0 0 4px',
            fontSize: '14px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {plan.title}
        </h2>
      )}

      {plan.title && (
        <hr
          style={{
            border: 'none',
            borderTop: '1px solid var(--border-default)',
            margin: '8px 0'
          }}
        />
      )}

      {plan.overview && (
        <p
          style={{
            ...planTextContainmentStyle,
            margin: '0 0 12px',
            fontSize: '13px',
            color: 'var(--text-secondary)',
            lineHeight: 1.6
          }}
        >
          {plan.overview}
        </p>
      )}

      {plan.todos.length > 0 && (
        <PlanTodoList todos={plan.todos} />
      )}
    </div>
  )
}

/**
 * Full shape-matched skeleton for the empty-streaming Plan tab: a title bar, two
 * overview lines, and four todo rows. Carries the accessible "drafting" label so
 * screen readers still hear the loading state that the visible spinner used to
 * convey.
 */
function PlanDraftSkeleton({ label }: { label: string }): JSX.Element {
  return (
    <div role="status" aria-busy="true" aria-label={label} style={planScrollContainerStyle}>
      <Skeleton width="58%" height={14} style={{ marginBottom: '8px' }} />
      <hr
        style={{
          border: 'none',
          borderTop: '1px solid var(--border-default)',
          margin: '0 0 12px'
        }}
      />
      <Skeleton width="100%" height={11} style={{ marginBottom: '7px' }} />
      <Skeleton width="82%" height={11} style={{ marginBottom: '18px' }} />
      <PlanDraftTodoSkeleton count={4} />
    </div>
  )
}

// Widths cycle so stacked skeleton todo rows read as varied content, not a grid.
const PLAN_SKELETON_TODO_WIDTHS = ['70%', '55%', '62%', '44%']

/**
 * A stack of placeholder todo rows (status circle + text bar) used both for the
 * full draft skeleton and as the trailing "still streaming" rows in the
 * partial-content state.
 */
function PlanDraftTodoSkeleton({
  count,
  style
}: {
  count: number
  style?: CSSProperties
}): JSX.Element {
  return (
    <div
      aria-hidden="true"
      style={{ display: 'flex', flexDirection: 'column', gap: '10px', ...style }}
    >
      {Array.from({ length: count }, (_, index) => (
        <div key={index} style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <Skeleton width={16} height={16} circle />
          <Skeleton
            width={PLAN_SKELETON_TODO_WIDTHS[index % PLAN_SKELETON_TODO_WIDTHS.length]}
            height={11}
          />
        </div>
      ))}
    </div>
  )
}

function PlanTodoList({ todos }: { todos: PlanTodoItem[] }): JSX.Element {
  return (
    <ul
      style={{
        listStyle: 'none',
        margin: 0,
        padding: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: '4px'
      }}
    >
      {todos.map((todo) => (
        <PlanTodoItemRow key={todo.id} todo={todo} />
      ))}
    </ul>
  )
}

function normalizeStreamingTodos(
  todos: Array<{ id?: string; content?: string; status?: PlanTodoStatus | string }>
): PlanTodoItem[] {
  return todos
    .map((todo, index) => ({
      id: typeof todo.id === 'string' && todo.id.trim().length > 0 ? todo.id : `todo-${index}`,
      content: typeof todo.content === 'string' ? todo.content : '',
      status: normalizeTodoStatus(todo.status)
    }))
    .filter((todo) => todo.content.trim().length > 0)
}

function normalizeTodoStatus(status: unknown): PlanTodoStatus {
  return status === 'in_progress' || status === 'completed' || status === 'cancelled'
    ? status
    : 'pending'
}

interface PlanTodoItemRowProps {
  todo: PlanTodoItem
}

function PlanTodoItemRow({ todo }: PlanTodoItemRowProps): JSX.Element {
  const isCancelled = todo.status === 'cancelled'

  return (
    <li
      style={{
        display: 'flex',
        alignItems: 'flex-start',
        gap: '8px',
        minWidth: 0,
        fontSize: '13px',
        lineHeight: 1.5
      }}
    >
      <PlanTodoStatusIcon status={todo.status} />
      <span
        style={{
          ...planTextContainmentStyle,
          color: isCancelled ? 'var(--text-dimmed)' : 'var(--text-primary)',
          textDecoration: isCancelled ? 'line-through' : 'none'
        }}
      >
        {todo.content}
      </span>
    </li>
  )
}

const planScrollContainerStyle: CSSProperties = {
  padding: '16px',
  overflowY: 'auto',
  height: '100%',
  minWidth: 0,
  maxWidth: '100%',
  boxSizing: 'border-box'
}

const planTextContainmentStyle: CSSProperties = {
  minWidth: 0,
  maxWidth: '100%',
  overflowWrap: 'anywhere',
  wordBreak: 'break-word'
}
