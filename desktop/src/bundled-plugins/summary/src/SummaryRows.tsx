import type { JSX, ReactNode } from 'react'

export const GLYPH = { size: 16, strokeWidth: 1.75 } as const

export function Row({
  leading,
  label,
  inline,
  title,
  ariaExpanded,
  muted,
  onClick
}: {
  leading: ReactNode
  label: string
  inline?: string
  title?: string
  ariaExpanded?: boolean
  muted?: boolean
  onClick?: () => void
}): JSX.Element {
  const content = (
    <>
      <span className="conversation-summary-row__leading" aria-hidden="true">{leading}</span>
      <span className="conversation-summary-row__label">{label}</span>
      {inline ? <span className="conversation-summary-row__inline">{inline}</span> : null}
    </>
  )
  if (!onClick) return <li className="conversation-summary-row" title={title}>{content}</li>
  return (
    <li>
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
