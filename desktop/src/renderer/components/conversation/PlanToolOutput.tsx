import { MarkdownRenderer } from './MarkdownRenderer'
import { translate, type AppLocale } from '../../../shared/locales'
import { PlanTodoStatusIcon } from '../plan/PlanTodoStatusIcon'

interface PlanToolOutputTodo {
  id: string
  content: string
  status: 'pending' | 'in_progress' | 'completed' | 'cancelled'
}

interface PlanToolOutputProps {
  itemId: string
  title: string
  overview: string
  content: string
  todos: PlanToolOutputTodo[]
  locale: AppLocale
}

export function PlanToolOutput({
  itemId,
  title,
  overview,
  content,
  todos,
  locale
}: PlanToolOutputProps): JSX.Element {
  return (
    <div
      className="selectable"
      data-plan-item-id={itemId}
      style={{
        minWidth: 0,
        maxWidth: '100%',
        boxSizing: 'border-box',
        fontSize: '12px',
        lineHeight: 1.55,
        color: 'var(--text-secondary)',
        overflowWrap: 'anywhere',
        wordBreak: 'break-word'
      }}
    >
      {title && (
        <h3
          style={{
            margin: '0 0 6px',
            fontSize: '13px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {title}
        </h3>
      )}

      {overview && (
        <div style={{ marginBottom: '10px' }}>
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginBottom: '4px' }}>
            {translate(locale, 'toolCall.plan.overviewLabel')}
          </div>
          <p
            style={{
              margin: 0,
              minWidth: 0,
              whiteSpace: 'pre-wrap',
              color: 'var(--text-secondary)',
              overflowWrap: 'anywhere',
              wordBreak: 'break-word'
            }}
          >
            {overview}
          </p>
        </div>
      )}

      {content && (
        <div style={{ marginBottom: todos.length > 0 ? '12px' : 0 }}>
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginBottom: '6px' }}>
            {translate(locale, 'toolCall.plan.contentLabel')}
          </div>
          <MarkdownRenderer content={content} containOverflow />
        </div>
      )}

      {todos.length > 0 && (
        <div>
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginBottom: '6px' }}>
            {translate(locale, 'toolCall.plan.todosLabel')}
          </div>
          <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'grid', gap: '4px' }}>
            {todos.map((todo) => {
              const isCancelled = todo.status === 'cancelled'
              return (
                <li key={todo.id} style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                  <PlanTodoStatusIcon status={todo.status} />
                  <span
                    style={{
                      minWidth: 0,
                      color: isCancelled ? 'var(--text-dimmed)' : 'var(--text-primary)',
                      textDecoration: isCancelled ? 'line-through' : 'none',
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
        </div>
      )}
    </div>
  )
}
