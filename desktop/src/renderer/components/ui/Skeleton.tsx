import type { CSSProperties, JSX } from 'react'

const SKELETON_ANIMATION = 'skeleton-pulse 1.4s ease-in-out infinite'

export interface SkeletonProps {
  /** CSS width. Numbers are treated as px. Defaults to full width. */
  width?: number | string
  height?: number | string
  /** Corner radius. Ignored when `circle` is set. */
  radius?: number | string
  circle?: boolean
  style?: CSSProperties
}

export function Skeleton({
  width = '100%',
  height = 12,
  radius = 4,
  circle = false,
  style
}: SkeletonProps): JSX.Element {
  return (
    <span
      aria-hidden="true"
      style={{
        display: 'block',
        flexShrink: 0,
        width,
        height,
        borderRadius: circle ? '50%' : radius,
        backgroundColor: 'var(--bg-tertiary)',
        animation: SKELETON_ANIMATION,
        ...style
      }}
    />
  )
}

export interface SkeletonRowProps {
  /** Size (px) of the leading media block; omit for a text-only row. */
  media?: number
  mediaCircle?: boolean
  mediaRadius?: number
  /** Width of each stacked text line; array length controls the line count. */
  lines?: Array<number | string>
  lineHeight?: number
  lineGap?: number
  style?: CSSProperties
}

export function SkeletonRow({
  media,
  mediaCircle = false,
  mediaRadius = 8,
  lines = ['62%', '38%'],
  lineHeight = 12,
  lineGap = 6,
  style
}: SkeletonRowProps): JSX.Element {
  return (
    <div
      aria-hidden="true"
      style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0, ...style }}
    >
      {media != null && (
        <Skeleton width={media} height={media} circle={mediaCircle} radius={mediaRadius} />
      )}
      <div
        style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: lineGap }}
      >
        {lines.map((lineWidth, index) => (
          <Skeleton key={index} width={lineWidth} height={lineHeight} />
        ))}
      </div>
    </div>
  )
}

export interface SkeletonListProps {
  count?: number
  gap?: number
  rowProps?: SkeletonRowProps
  rowStyle?: CSSProperties
  /** Accessible label announced while loading (e.g. localized "Loading…"). */
  ariaLabel?: string
  style?: CSSProperties
}

export function SkeletonList({
  count = 4,
  gap = 10,
  rowProps,
  rowStyle,
  ariaLabel,
  style
}: SkeletonListProps): JSX.Element {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label={ariaLabel}
      style={{ display: 'flex', flexDirection: 'column', gap, ...style }}
    >
      {Array.from({ length: count }, (_, index) => (
        <SkeletonRow
          key={index}
          {...rowProps}
          style={{ ...rowProps?.style, ...rowStyle }}
        />
      ))}
    </div>
  )
}

const catalogGridStyle: CSSProperties = {
  maxWidth: '760px',
  margin: '0 auto',
  display: 'grid',
  gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
  columnGap: '34px',
  rowGap: '18px'
}

const catalogItemStyle: CSSProperties = {
  height: '58px',
  padding: '0 8px'
}

/** Mirrors the catalog `compactGrid`: a 58px icon plus title and subtitle per cell. */
export function SkeletonCatalogGrid({
  count = 6,
  ariaLabel,
  style
}: {
  count?: number
  ariaLabel?: string
  style?: CSSProperties
}): JSX.Element {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label={ariaLabel}
      style={{ ...catalogGridStyle, ...style }}
    >
      {Array.from({ length: count }, (_, index) => (
        <SkeletonRow
          key={index}
          media={28}
          mediaRadius={8}
          lines={[index % 2 === 0 ? '66%' : '52%', index % 2 === 0 ? '40%' : '48%']}
          lineHeight={11}
          style={catalogItemStyle}
        />
      ))}
    </div>
  )
}
