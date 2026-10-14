import type { DesktopPluginIconProps } from '@dotcraft/plugin'
import type { JSX } from 'react'

export function SummaryIcon({ size = 16, style, ...rest }: DesktopPluginIconProps): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      style={style}
      {...rest}
    >
      <circle cx="6" cy="6.75" r="2.75" />
      <circle cx="6" cy="17.25" r="2.75" />
      <path d="M12.5 6.75h8M12.5 17.25h8" />
    </svg>
  )
}
