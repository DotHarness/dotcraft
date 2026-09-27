import { useMemo, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import {
  buildStreamingPlanDraft,
  selectStreamingPlanItemId,
  selectStreamingPlanRawArgs,
  useConversationStore
} from '../../stores/conversationStore'
import type { PlanTodoItem, PlanTodoStatus } from '../../stores/conversationStore'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { PlanTodoStatusIcon } from '../plan/PlanTodoStatusIcon'
import { Skeleton } from '../ui/Skeleton'

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
            <PlanOverview content={streamingDraft.overview} />
          )}
          {streamingTodos.length > 0 && (
            <PlanTodoList todos={streamingTodos} />
          )}
          <PlanDraftTodoSkeleton
            count={2}
            style={{ marginTop: streamingTodos.length > 0 ? '6px' : '0' }}
          />
        </div>
      )
    }

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
        <PlanOverview content={plan.overview} />
      )}

      {plan.todos.length > 0 && (
        <PlanTodoList todos={plan.todos} />
      )}
    </div>
  )
}

function PlanOverview({ content }: { content: string }): JSX.Element {
  return (
    <div style={{ marginBottom: '12px', ...planTextContainmentStyle }}>
      <MarkdownRenderer content={content} containOverflow enableMermaid={false} />
    </div>
  )
}

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

const PLAN_SKELETON_TODO_WIDTHS = ['70%', '55%', '62%', '44%']

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
