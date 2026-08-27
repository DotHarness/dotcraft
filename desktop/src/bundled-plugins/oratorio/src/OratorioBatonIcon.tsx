import type { DesktopPluginIconProps } from '@dotcraft/plugin'

/** Compact Oratorio identity mark for navigation slots. */
export function OratorioBatonIcon({
  size = 24,
  strokeWidth = 2,
  ...props
}: DesktopPluginIconProps): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeLinecap="round"
      strokeLinejoin="round"
      {...props}
    >
      <path d="M13.3 4.2A8.1 8.1 0 0 0 4.3 16.2" strokeWidth={strokeWidth} />
      <path d="M7.6 19.7A8.1 8.1 0 0 0 19.3 10.3" strokeWidth={strokeWidth} />
      <path d="M4.8 20.8 16.2 7.1" strokeWidth={strokeWidth} />
      <circle cx="18.2" cy="4.6" r="3.05" strokeWidth={strokeWidth} />
    </svg>
  )
}
