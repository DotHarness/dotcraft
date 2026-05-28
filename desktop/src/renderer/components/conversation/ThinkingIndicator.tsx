import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ToolCollapseChevron } from './ToolCollapseChevron'

interface ThinkingIndicatorProps {
  /** Elapsed reasoning time in seconds */
  elapsedSeconds?: number
  /** Full reasoning text — shown when expanded */
  reasoning?: string
  /** True while the agent is still reasoning (live streaming) */
  streaming?: boolean
}

/**
 * Collapsible "Thought Xs" indicator for agent reasoning.
 * Collapsed by default; click chevron to show/hide full text.
 * Spec §10.3.3
 */
export function ThinkingIndicator({
  elapsedSeconds,
  reasoning,
  streaming = false
}: ThinkingIndicatorProps): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const [hovered, setHovered] = useState(false)
  const canExpand = !!reasoning

  const label = streaming
    ? t('conversation.thinking.streaming')
    : t('conversation.thinking.completed', { seconds: elapsedSeconds ?? 0 })
  const rowColor = hovered || expanded ? 'var(--text-secondary)' : 'var(--text-dimmed)'

  return (
    <div>
      {/* Summary line */}
      <ActionTooltip
        label={
          canExpand
            ? t('conversation.thinking.expandTooltip')
            : t('conversation.thinking.statusTooltip')
        }
        placement="top"
      >
        <button
          onClick={() => canExpand && setExpanded((v) => !v)}
          onMouseEnter={() => setHovered(true)}
          onMouseLeave={() => setHovered(false)}
          onFocus={() => setHovered(true)}
          onBlur={() => setHovered(false)}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            width: '100%',
            background: 'none',
            border: 'none',
            borderRadius: '4px',
            cursor: canExpand ? 'pointer' : 'default',
            padding: '3px 6px',
            color: rowColor,
            fontSize: '12px',
            textAlign: 'left'
          }}
          aria-expanded={expanded}
        >
          <span
            data-testid="tool-row-title-group"
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '3px',
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
              <ToolCollapseChevron expanded={expanded} visible={hovered || expanded} />
            )}
          </span>
        </button>
      </ActionTooltip>

      {/* Expanded reasoning text */}
      {expanded && reasoning && (
        <div
          style={{
            marginTop: '4px',
            padding: '8px 12px',
            borderLeft: '2px solid var(--border-default)',
            background: 'transparent',
            color: 'var(--text-dimmed)',
            fontStyle: 'italic',
            fontSize: '13px',
            lineHeight: 1.6,
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
