import type { CSSProperties } from 'react'
import { useLocale } from '../../contexts/LocaleContext'

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
  const locale = useLocale()
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
        gap: '4px',
        flexShrink: 0,
        fontFamily: 'inherit',
        fontSize: 'inherit',
        fontWeight: 'inherit',
        lineHeight: 'inherit',
        ...style
      }}
    >
      {additions > 0 && <span style={{ color: additionColor }}>+{additions.toLocaleString(locale)}</span>}
      {deletions > 0 && <span style={{ color: deletionColor }}>-{deletions.toLocaleString(locale)}</span>}
    </span>
  )
}
