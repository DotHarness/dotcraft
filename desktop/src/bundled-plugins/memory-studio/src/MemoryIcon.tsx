import type { DesktopPluginIconProps } from '@dotcraft/plugin'

import './memory-icon.css'

export function MemoryIcon({
  size = 24,
  strokeWidth = 2,
  ...props
}: DesktopPluginIconProps): JSX.Element {
  return (
    <svg
      className="memory-studio-icon"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      {...props}
    >
      <path d="M5 5.5A2.5 2.5 0 0 1 7.5 3h9A2.5 2.5 0 0 1 19 5.5V21l-7-4.2L5 21Z" />
      <path
        className="memory-studio-icon__spark"
        d="M12 6c0 2.6 1.15 3.75 3.75 3.75-2.6 0-3.75 1.15-3.75 3.75 0-2.6-1.15-3.75-3.75-3.75 2.6 0 3.75-1.15 3.75-3.75Z"
        fill="currentColor"
        stroke="none"
      />
    </svg>
  )
}
