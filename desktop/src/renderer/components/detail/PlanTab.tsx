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
    if (streamingDraft?.overview || streamingTodos.length > 0) {
      return (
        <div style={planScrollContainerStyle}>
          <StreamingDraftBadge label={t('plan.streamingDraftBadge')} />
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
        </div>
      )
    }

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
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            fontSize: '13px',
            color: 'var(--text-dimmed)'
          }}
        >
          <span
            className="animate-spin-custom"
            style={{
              display: 'inline-block',
              width: '10px',
              height: '10px',
              borderRadius: '50%',
              border: '2px solid var(--border-active)',
              borderTopColor: 'var(--accent)'
            }}
          />
          <span>{t('plan.streamingDraftBadge')}</span>
        </div>
      </div>
    )
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

function StreamingDraftBadge({ label }: { label: string }): JSX.Element {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '8px',
        fontSize: '13px',
        color: 'var(--text-dimmed)',
        marginBottom: '12px'
      }}
    >
      <span
        className="animate-spin-custom"
        style={{
          display: 'inline-block',
          width: '10px',
          height: '10px',
          borderRadius: '50%',
          border: '2px solid var(--border-active)',
          borderTopColor: 'var(--accent)'
        }}
      />
      <span>{label}</span>
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
