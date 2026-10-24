import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ToolCollapseChevron } from './ToolDisclosure'

export function ThinkingIndicator({ reasoning }: { reasoning?: string }): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const canExpand = !!reasoning

  return (
    <div className="dc-thinking-indicator">
      <ActionTooltip
        label={
          canExpand
            ? t('conversation.thinking.expandTooltip')
            : t('conversation.thinking.statusTooltip')
        }
        placement="top"
      >
        <button
          type="button"
          className="dc-thinking-row"
          data-expandable={canExpand ? 'true' : undefined}
          onClick={() => canExpand && setExpanded((v) => !v)}
          aria-expanded={canExpand && expanded}
        >
          <span
            data-testid="tool-row-title-group"
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '6px',
              flex: '0 1 auto',
              minWidth: 0,
              maxWidth: '100%'
            }}
          >
            <ThoughtIcon />
            <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {t('conversation.thinking.completed')}
            </span>
            {canExpand && (
              <ToolCollapseChevron expanded={expanded} visible={expanded} />
            )}
          </span>
        </button>
      </ActionTooltip>

      {expanded && reasoning && (
        <div
          className="selectable"
          style={{
            marginTop: '4px',
            padding: '8px 12px',
            borderLeft: '2px solid var(--border-default)',
            background: 'transparent',
            color: 'var(--text-dimmed)',
            fontStyle: 'italic',
            fontSize: 'var(--conversation-font-size)',
            lineHeight: 'var(--conversation-line-height)',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word'
          }}
        >
          {reasoning}
        </div>
      )}
    </div>
  )
}

function ThoughtIcon(): JSX.Element {
  return (
    <svg className="dc-tool-row-icon" width={16} height={16} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx={12} cy={12} r={8} stroke="currentColor" strokeWidth={1.6} />
      {[0, 120, 240].map((angle) => (
        <circle key={angle} cx={12} cy={4} r={2.4} transform={`rotate(${angle} 12 12)`} fill="currentColor" />
      ))}
      <circle cx={12} cy={12} r={2.2} fill="currentColor" />
    </svg>
  )
}
