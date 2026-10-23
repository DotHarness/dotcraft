import type { JSX, ReactNode } from 'react'

export const GLYPH = { size: 16, strokeWidth: 1.75 } as const

export function Row({
  leading,
  label,
  inline,
  meta,
  title,
  ariaExpanded,
  muted,
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
    return (
      <li className="conversation-summary-row" title={trailing ? undefined : title}>
        {content}
        {trailingSlot}
      </li>
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
        onClick={onClick}
      >
        {content}
      </button>
      {trailingSlot}
    </li>
  )
}

export function Section({
  title,
  actions,
  children
}: {
  title?: string
  actions?: ReactNode
  children: ReactNode
}): JSX.Element {
  const heading = title ? <h3 className="conversation-summary-section__title">{title}</h3> : null
  return (
    <section className="conversation-summary-section" aria-label={title}>
      {actions ? (
        <div className="conversation-summary-section__header">
          {heading}
          <div className="conversation-summary-section__actions">{actions}</div>
        </div>
      ) : heading}
      <ul className="conversation-summary-rows">{children}</ul>
    </section>
  )
}
