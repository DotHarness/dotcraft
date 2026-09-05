import type { CSSProperties } from 'react'

/**
 * Two tiers of supporting text: `description` under a page or card title, `hint`
 * under a row label. Both use --text-secondary; --text-dimmed is reserved for
 * genuinely tertiary content via `settingsMetaTextStyle`.
 */

/** Class on the settings scroll container. Scopes the per-locale type scale. */
export const SETTINGS_SURFACE_CLASS = 'dc-settings-surface'

/** 18px page heading (panel title). */
export function settingsPageTitleStyle(): CSSProperties {
  return {
    fontSize: 'var(--type-page-title-size)',
    lineHeight: 'var(--type-page-title-line-height)',
    fontWeight: 'var(--type-page-title-weight)' as CSSProperties['fontWeight'],
    color: 'var(--text-primary)'
  }
}

/** 15px card / group heading. */
export function settingsHeadingStyle(): CSSProperties {
  return {
    fontSize: 'var(--type-heading-size)',
    lineHeight: 'var(--type-heading-line-height)',
    fontWeight: 'var(--type-heading-weight)' as CSSProperties['fontWeight'],
    color: 'var(--text-primary)'
  }
}

/** 12px label above a field stack inside a row. */
export function settingsSectionLabelStyle(): CSSProperties {
  return {
    display: 'block',
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-line-height)',
    fontWeight: 600,
    color: 'var(--text-secondary)',
    marginBottom: '6px'
  }
}

/** 13px row label. */
export function settingsLabelStyle(): CSSProperties {
  return {
    display: 'block',
    fontSize: 'var(--type-ui-size)',
    lineHeight: 'var(--type-ui-line-height)',
    fontWeight: 600,
    color: 'var(--text-primary)'
  }
}

/**
 * 12px supporting copy under a page or card title.
 * `marginTop` is omitted so callers control the gap; use `withGap` for the
 * standard 4px offset under a heading.
 */
export function settingsDescriptionStyle(withGap = true): CSSProperties {
  return {
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-prose-line-height)',
    color: 'var(--text-secondary)',
    ...(withGap ? { marginTop: '4px' } : null)
  }
}

/** 11px supporting copy under a row label, or a card footnote. */
export function settingsHintStyle(withGap = true): CSSProperties {
  return {
    fontSize: 'var(--type-hint-size)',
    lineHeight: 'var(--type-hint-line-height)',
    color: 'var(--text-secondary)',
    ...(withGap ? { marginTop: '4px' } : null)
  }
}

/**
 * 11px tertiary metadata — version strings, timestamps, "last synced" notes.
 * Same size as a hint, one step dimmer, because it is not describing a control.
 */
export function settingsMetaTextStyle(withGap = false): CSSProperties {
  return {
    fontSize: 'var(--type-hint-size)',
    lineHeight: 'var(--type-hint-line-height)',
    color: 'var(--text-dimmed)',
    ...(withGap ? { marginTop: '4px' } : null)
  }
}

/**
 * 12px dimmed copy that stands on its own rather than describing a control:
 * empty states, "loading…" placeholders, and inline result detail. Standalone
 * copy keeps the larger size; only nested metadata drops to the hint tier.
 */
export function settingsPlaceholderStyle(): CSSProperties {
  return {
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-line-height)',
    color: 'var(--text-dimmed)'
  }
}

/** 11px inline error text under a control. */
export function settingsErrorTextStyle(withGap = true): CSSProperties {
  return {
    fontSize: 'var(--type-hint-size)',
    lineHeight: 'var(--type-hint-line-height)',
    color: 'var(--error)',
    ...(withGap ? { marginTop: '4px' } : null)
  }
}
