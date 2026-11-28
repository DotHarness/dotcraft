import { useState, type JSX, type ReactNode, type Ref } from 'react'
import { DisclosureChevron } from '../../ui/DisclosureChevron'
import { setSectionExpanded, useSummaryView } from './view'

export const GLYPH = { size: 16, strokeWidth: 1.75 } as const

export function Row({
  leading,
  label,
  inline,
  meta,
  title,
  ariaExpanded,
  muted,
  interaction = 'background',
  trailing,
  onClick
}: {
  leading: ReactNode
  label: string
  inline?: string
  meta?: ReactNode
  title?: string
  ariaExpanded?: boolean
  muted?: boolean
  interaction?: 'background' | 'foreground'
  trailing?: ReactNode
  onClick?: () => void
}): JSX.Element {
  const content = (
    <>
      <span className="conversation-summary-row__leading" aria-hidden="true">{leading}</span>
      <span className="conversation-summary-row__label" title={trailing ? title : undefined}>{label}</span>
      {inline ? <span className="conversation-summary-row__inline">{inline}</span> : null}
      {meta ? <span className="conversation-summary-row__meta">{meta}</span> : null}
    </>
  )
  const trailingSlot = trailing ? <span className="conversation-summary-row__trailing">{trailing}</span> : null
  if (!onClick) {
    return trailing ? (
      <li className="conversation-summary-row-item">
        <div className="conversation-summary-row">{content}</div>
        {trailingSlot}
      </li>
    ) : (
      <li className="conversation-summary-row" title={title}>{content}</li>
    )
  }
  return (
    <li className={trailing ? 'conversation-summary-row-item' : undefined}>
      <button
        type="button"
        className="conversation-summary-row conversation-summary-row--action"
        title={title}
        aria-expanded={ariaExpanded}
        data-muted={muted || undefined}
        data-interaction={interaction === 'foreground' ? 'foreground' : undefined}
        onClick={onClick}
      >
        {content}
      </button>
      {trailingSlot}
    </li>
  )
}

export function Section({
  sectionKey,
  sectionRef,
  title,
  count,
  collapsedSummary,
  actions,
  footer,
  defaultExpanded = true,
  hideWhenEmpty,
  listRef,
  children
}: {
  sectionKey?: string
  sectionRef?: Ref<HTMLElement>
  title?: string
  count?: number
  collapsedSummary?: ReactNode
  actions?: ReactNode
  footer?: ReactNode
  defaultExpanded?: boolean
  /** Hides the section while its rows render nothing, for rows contributed by plugins. */
  hideWhenEmpty?: boolean
  listRef?: Ref<HTMLUListElement>
  children: ReactNode
}): JSX.Element {
  const { sectionExpanded } = useSummaryView()
  const [localExpanded, setLocalExpanded] = useState(defaultExpanded)
  const persisted = sectionKey ? sectionExpanded[sectionKey] : undefined
  const expanded = !title ? true : persisted ?? localExpanded
  const toggle = (): void => {
    const next = !expanded
    if (sectionKey) setSectionExpanded(sectionKey, next)
    else setLocalExpanded(next)
  }

  const list = <ul ref={listRef} className="conversation-summary-rows">{children}</ul>

  if (!title) {
    return (
      <section ref={sectionRef} className="conversation-summary-section" data-hide-empty={hideWhenEmpty || undefined}>
        {list}
      </section>
    )
  }

  return (
    <section
      ref={sectionRef}
      className="conversation-summary-section"
      aria-label={title}
      data-hide-empty={hideWhenEmpty || undefined}
      data-collapsed={!expanded || undefined}
    >
      <div className="conversation-summary-section__header">
        <button
          type="button"
          className="conversation-summary-section__toggle"
          aria-expanded={expanded}
          onClick={toggle}
        >
          <h3 className="conversation-summary-section__title">{title}</h3>
          {!expanded && count !== undefined && count > 0 ? (
            <span className="conversation-summary-section__count" aria-hidden="true">
              <span>·</span>
              <span>{count}</span>
            </span>
          ) : null}
          <DisclosureChevron expanded={expanded} />
          {!expanded && collapsedSummary ? (
            <span className="conversation-summary-section__collapsed-summary">{collapsedSummary}</span>
          ) : null}
        </button>
        {actions ? <div className="conversation-summary-section__actions">{actions}</div> : null}
      </div>
      <div
        className="conversation-summary-section__body"
        data-expanded={expanded || undefined}
        aria-hidden={!expanded || undefined}
        inert={!expanded ? true : undefined}
      >
        <div className="conversation-summary-section__body-inner">
          {list}
          {footer}
        </div>
      </div>
    </section>
  )
}
