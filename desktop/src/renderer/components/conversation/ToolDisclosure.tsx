import { ChevronRight } from 'lucide-react'
import { useRef, type JSX, type ReactNode } from 'react'

interface ToolDisclosureProps {
  expanded: boolean
  onToggle: () => void
  expandable?: boolean
  title: ReactNode
  trailing?: ReactNode
  tone?: 'error'
  variant?: 'turn'
  onHoverChange?: (hovered: boolean) => void
  children?: ReactNode
}

/**
 * The transcript's tool row. Its panel is a `<details>` whose `::details-content`
 * transitions `block-size` between `0` and the `auto` keyword, so nothing measures
 * height and a panel that streams keeps up on its own.
 */
export function ToolDisclosure({
  expanded,
  onToggle,
  expandable = true,
  title,
  trailing,
  tone,
  variant,
  onHoverChange,
  children
}: ToolDisclosureProps): JSX.Element {
  // Children mount in the same commit that sets `open`, so the first expansion still
  // transitions while a never-opened panel stays out of the DOM.
  const opened = useRef(false)
  if (expanded) opened.current = true

  const hoverProps = onHoverChange
    ? {
      onMouseEnter: () => onHoverChange(true),
      onMouseLeave: () => onHoverChange(false),
      onFocus: () => onHoverChange(true),
      onBlur: () => onHoverChange(false)
    }
    : {}

  if (!expandable) {
    return (
      <div className="dc-tool-row-static" data-testid="tool-row" data-expandable="false" data-tone={tone} data-variant={variant} {...hoverProps}>
        <span className="dc-tool-row-title" data-testid="tool-row-title-group">
          <span className="dc-tool-row-text">{title}</span>
        </span>
        {trailing ? <span className="dc-tool-row-trailing">{trailing}</span> : null}
      </div>
    )
  }

  return (
    <details
      className="dc-tool-row"
      data-tone={tone}
      data-variant={variant}
      open={expanded}
    >
      <summary
        data-testid="tool-row"
        data-expandable="true"
        // HTML-AAM maps a details' summary to `button` with an expanded state;
        // stating it keeps that contract explicit for tooling without the mapping.
        role="button"
        aria-expanded={expanded}
        // `open` is React's, so the native toggle is suppressed and the click drives
        // state instead. Enter and Space on a summary dispatch a click.
        onClick={(event) => {
          event.preventDefault()
          onToggle()
        }}
        {...hoverProps}
      >
        <span className="dc-tool-row-title" data-testid="tool-row-title-group">
          <span className="dc-tool-row-text">{title}</span>
          <ToolCollapseChevron expanded={expanded} />
        </span>
        {trailing ? <span className="dc-tool-row-trailing">{trailing}</span> : null}
      </summary>
      <div className="dc-tool-panel">{opened.current ? children : null}</div>
    </details>
  )
}

interface ToolCollapseChevronProps {
  expanded: boolean
  visible?: boolean
}

export function ToolCollapseChevron({
  expanded,
  visible
}: ToolCollapseChevronProps): JSX.Element {
  return (
    <span
      className="dc-tool-chevron"
      data-testid="tool-disclosure-icon"
      data-expanded={expanded ? 'true' : undefined}
      data-visible={visible ? 'true' : undefined}
      aria-hidden={visible === false}
    >
      <ChevronRight size={13} strokeWidth={1.8} aria-hidden />
    </span>
  )
}
