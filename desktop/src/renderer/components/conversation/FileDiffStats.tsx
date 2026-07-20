import type { CSSProperties } from 'react'

export type FileDiffStatsTone = 'semantic' | 'inherit' | 'dimmed'

interface FileDiffStatsProps {
  additions: number
  deletions: number
  tone?: FileDiffStatsTone
  testId?: string
  style?: CSSProperties
}

export function FileDiffStats({
  additions,
  deletions,
  tone = 'semantic',
  testId,
  style
}: FileDiffStatsProps): JSX.Element | null {
  if (additions <= 0 && deletions <= 0) return null

  const additionColor = tone === 'semantic'
    ? 'var(--success)'
    : tone === 'dimmed'
      ? 'var(--text-dimmed)'
      : 'currentColor'
  const deletionColor = tone === 'semantic'
    ? 'var(--error)'
    : tone === 'dimmed'
      ? 'var(--text-dimmed)'
      : 'currentColor'

  return (
    <span
      data-testid={testId}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 'var(--space-xs)',
        flexShrink: 0,
        fontFamily: 'inherit',
        fontSize: 'inherit',
        fontWeight: 'inherit',
        lineHeight: 'inherit',
        ...style
      }}
    >
      {additions > 0 && <span style={{ color: additionColor }}>+{additions}</span>}
      {deletions > 0 && <span style={{ color: deletionColor }}>-{deletions}</span>}
    </span>
  )
}
