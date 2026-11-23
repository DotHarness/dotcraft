import type { CSSProperties } from 'react'

const dotCraftFullLogoUrl = new URL('../../../../resources/dotcraft.svg', import.meta.url).toString()

interface DotCraftLogoProps {
  size?: number
  className?: string
  style?: CSSProperties
}

export function DotCraftLogo({ size = 32, className, style }: DotCraftLogoProps): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      style={style}
      aria-label="DotCraft logo"
    >
      <path
        fill="currentColor"
        d="M12,2a2,2 0,1 1,0 4a2,2 0,1 1,0 -4z"
      />
      <path fill="currentColor" d="M11.25,5.5h1.5v2.5h-1.5z" />
      <path fill="currentColor" d="M2.5,12h1.5v4h-1.5z M20,12h1.5v4h-1.5z" />
      <path
        fill="currentColor"
        fillRule="evenodd"
        clipRule="evenodd"
        d="M7.5,8h9a3.5,3.5 0,0 1,3.5 3.5v5a3.5,3.5 0,0 1,-3.5 3.5h-9a3.5,3.5 0,0 1,-3.5 -3.5v-5a3.5,3.5 0,0 1,3.5 -3.5z M7.6,11.1l1.1,-1.1l3.9,3.9l-3.9,3.9l-1.1,-1.1l2.8,-2.8z M13.5,16.4h3.5v1.5h-3.5z"
      />
    </svg>
  )
}

export function DotCraftFullLogo({ size = 32, className, style }: DotCraftLogoProps): JSX.Element {
  return (
    <img
      src={dotCraftFullLogoUrl}
      alt="DotCraft"
      width={size}
      height={size}
      className={className}
      style={{
        display: 'block',
        objectFit: 'contain',
        ...style
      }}
      draggable={false}
    />
  )
}
