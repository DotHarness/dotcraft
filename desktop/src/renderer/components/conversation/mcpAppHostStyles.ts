import type { McpUiStyles } from '@modelcontextprotocol/ext-apps/app-bridge'

/** The app sits on the host frame's `--bg-secondary`, so its fields and insets use `--bg-primary` as Desktop cards do. */
const TOKENS: Partial<Record<keyof McpUiStyles, string>> = {
  '--color-background-primary': '--bg-secondary',
  '--color-background-secondary': '--bg-primary',
  '--color-background-tertiary': '--bg-tertiary',
  '--color-background-inverse': '--text-primary',
  '--color-background-info': '--info-bg',
  '--color-background-danger': '--error-bg',
  '--color-background-success': '--success-bg',
  '--color-background-warning': '--warning-bg',
  '--color-background-disabled': '--bg-active',
  '--color-text-primary': '--text-primary',
  '--color-text-secondary': '--text-secondary',
  '--color-text-tertiary': '--text-tertiary',
  '--color-text-inverse': '--bg-primary',
  '--color-text-ghost': '--text-tertiary',
  '--color-text-info': '--info-text',
  '--color-text-danger': '--error-text',
  '--color-text-success': '--success-text',
  '--color-text-warning': '--warning-text',
  '--color-text-disabled': '--text-tertiary',
  '--color-border-primary': '--border-active',
  '--color-border-secondary': '--border-default',
  '--color-border-tertiary': '--border-default',
  '--color-border-inverse': '--text-primary',
  '--color-border-info': '--info',
  '--color-border-danger': '--error',
  '--color-border-success': '--success',
  '--color-border-warning': '--warning',
  '--color-border-disabled': '--border-default',
  '--color-ring-primary': '--border-active',
  '--color-ring-secondary': '--border-active',
  '--color-ring-inverse': '--text-primary',
  '--color-ring-info': '--accent',
  '--color-ring-danger': '--error',
  '--color-ring-success': '--success',
  '--color-ring-warning': '--warning',
  '--font-sans': '--font-ui',
  '--font-mono': '--font-mono',
  '--font-text-xs-size': '--type-hint-size',
  '--font-text-sm-size': '--type-secondary-size',
  '--font-text-md-size': '--type-ui-size',
  '--font-text-lg-size': '--type-body-size',
  '--font-text-xs-line-height': '--type-hint-line-height',
  '--font-text-sm-line-height': '--type-secondary-line-height',
  '--font-text-md-line-height': '--type-ui-line-height',
  '--font-text-lg-line-height': '--type-body-line-height',
  '--font-heading-sm-size': '--type-heading-size',
  '--font-heading-md-size': '--type-page-title-size',
  '--font-heading-lg-size': '--type-detail-title-size',
  '--border-radius-md': '--button-radius',
  '--border-radius-full': '--button-text-radius',
  '--shadow-sm': '--shadow-sm',
  '--shadow-md': '--shadow-md',
  '--shadow-lg': '--shadow-lg'
}

const FIXED: Partial<McpUiStyles> = {
  '--color-background-ghost': 'transparent',
  '--color-border-ghost': 'transparent',
  '--font-weight-normal': '400',
  '--font-weight-medium': '500',
  '--font-weight-semibold': '600',
  '--font-weight-bold': '700',
  '--border-radius-xs': '4px',
  '--border-radius-sm': '6px',
  '--border-radius-lg': '10px',
  '--border-radius-xl': '12px',
  '--border-width-regular': '1px'
}

export function mcpAppHostStyles(): McpUiStyles {
  const computed = getComputedStyle(document.documentElement)
  const styles: Partial<McpUiStyles> = { ...FIXED }
  for (const [key, token] of Object.entries(TOKENS) as [keyof McpUiStyles, string][]) {
    const value = computed.getPropertyValue(token).trim()
    if (value) styles[key] = value
  }
  return styles as McpUiStyles
}
