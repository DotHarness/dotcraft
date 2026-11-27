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
      <path d="M12 4H7a2.5 2.5 0 0 0-2.5 2.5V21l7-4.1 7 4.1V12" />
      <g className="memory-studio-icon__pop">
        <g className="memory-studio-icon__spin">
          <path
            d="M17.5 2.1c0 3 1.4 4.4 4.4 4.4-3 0-4.4 1.4-4.4 4.4 0-3-1.4-4.4-4.4-4.4 3 0 4.4-1.4 4.4-4.4Z"
            fill="currentColor"
            stroke="none"
          />
        </g>
      </g>
    </svg>
  )
}
