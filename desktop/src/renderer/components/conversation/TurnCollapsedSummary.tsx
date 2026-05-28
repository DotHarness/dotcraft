import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { formatDurationShort } from '../../utils/formatDurationShort'
import { CollapsibleContent } from './CollapsibleContent'
import { ToolCollapseChevron } from './ToolCollapseChevron'

interface TurnCollapsedSummaryProps {
  elapsedMs: number
  children: ReactNode
}

export function TurnCollapsedSummary({
  elapsedMs,
  children
}: TurnCollapsedSummaryProps): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const [hovered, setHovered] = useState(false)
  const [renderExpanded, setRenderExpanded] = useState(false)

  useEffect(() => {
    if (expanded) {
      setRenderExpanded(true)
    }
  }, [expanded])

  const duration = formatDurationShort(elapsedMs)
  const label = t('conversation.turnCollapsed.processed', { duration })
  const rowColor = hovered || expanded ? 'var(--text-secondary)' : 'var(--text-dimmed)'

  return (
    <div>
      <button
        onClick={() => setExpanded((value) => !value)}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setHovered(true)}
        onBlur={() => setHovered(false)}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          width: '100%',
          padding: '3px 6px',
          background: 'transparent',
          border: 'none',
          borderBottom: '1px solid var(--border-subtle, rgba(127,127,127,0.18))',
          color: rowColor,
          fontSize: '12px',
          textAlign: 'left',
          cursor: 'pointer'
        }}
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
          <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {label}
          </span>
          <ToolCollapseChevron expanded={expanded} visible={hovered || expanded} />
        </span>
      </button>
      <CollapsibleContent
        expanded={expanded}
        renderExpanded={renderExpanded}
        setRenderExpanded={setRenderExpanded}
      >
        <div style={{ paddingTop: '6px' }}>
          {children}
        </div>
      </CollapsibleContent>
    </div>
  )
}
