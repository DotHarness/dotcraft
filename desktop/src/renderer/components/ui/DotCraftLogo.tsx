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
      viewBox="0 0 48 48"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      style={style}
      aria-label="DotCraft logo"
    >
      <circle cx="24" cy="2.5" r="2.8" fill="currentColor" />
      <line
        x1="24"
        y1="5.3"
        x2="24"
        y2="10.5"
        stroke="currentColor"
        strokeWidth="2.5"
        strokeLinecap="round"
      />
      <path
        d="M5.5 32V22C5.5 13 13.5 10.5 24 10.5 C34.5 10.5 42.5 13 42.5 22V32C42.5 39 34.5 41.5 24 41.5 C13.5 41.5 5.5 39 5.5 32Z"
        stroke="currentColor"
        strokeWidth="2.8"
        strokeLinejoin="round"
      />
      <path
        d="M15.5 22L22 27L15.5 32"
        stroke="currentColor"
        strokeWidth="3.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M26 32H34"
        stroke="currentColor"
        strokeWidth="3.8"
        strokeLinecap="round"
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
