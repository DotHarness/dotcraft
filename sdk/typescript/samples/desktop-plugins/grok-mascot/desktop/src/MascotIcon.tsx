import type { DesktopPluginIconProps } from '@dotcraft/plugin'
import type { JSX } from 'react'

export function MascotIcon({ size = 16, style, ...rest }: DesktopPluginIconProps): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      style={style}
      {...rest}
    >
      <path d="M12 2.6c6.2 0 9.4 3.2 9.4 9.4s-3.2 9.4-9.4 9.4S2.6 18.2 2.6 12 5.8 2.6 12 2.6Z" />
      <ellipse cx="9" cy="11.2" rx="1.35" ry="1" fill="currentColor" stroke="none" />
      <ellipse cx="15" cy="11.2" rx="1.35" ry="1" fill="currentColor" stroke="none" />
    </svg>
  )
}
