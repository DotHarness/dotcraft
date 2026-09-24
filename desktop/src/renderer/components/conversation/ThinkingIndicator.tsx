import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ToolCollapseChevron } from './ToolDisclosure'

interface ThinkingIndicatorProps {
  elapsedSeconds?: number
  reasoning?: string
  /** True while the agent is still reasoning (live streaming) */
  streaming?: boolean
}

/** Collapsible "Thought Xs" indicator for agent reasoning. Spec §10.3.3. */
export function ThinkingIndicator({
  elapsedSeconds,
  reasoning,
  streaming = false
}: ThinkingIndicatorProps): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const canExpand = !!reasoning

  const label = streaming
    ? t('conversation.thinking.streaming')
    : t('conversation.thinking.completed', { seconds: elapsedSeconds ?? 0 })

  return (
    <div>
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
          aria-expanded={expanded}
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
            <span
              className={streaming ? 'tool-running-gradient-text' : undefined}
              style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              {label}
            </span>
            {canExpand && (
              <ToolCollapseChevron expanded={expanded} visible={expanded} />
            )}
          </span>
        </button>
      </ActionTooltip>

      {expanded && reasoning && (
        <div
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
