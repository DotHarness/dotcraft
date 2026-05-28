import type { SubAgentEntry } from '../../types/toolCall'

/**
 * Formats a token count into a compact human-readable string.
 * e.g. 1234 → "1.2k", 500 → "500"
 */
function formatTokenCount(count: number): string {
  if (count >= 1000) return `${(count / 1000).toFixed(1)}k`
  return String(count)
}

/**
 * Live SubAgent progress block. Renders either:
 *   - Active table: one row per sub-agent showing spinner/dot, label, current tool, tokens
 *   - Collapsed summary: when all sub-agents complete
 *
 * Only renders when entries.length > 0.
 * Renders subagent progress events in the conversation stream.
 */
export interface SubAgentProgressBlockProps {
  /** SubAgent entries to render for the current turn position. */
  entries: SubAgentEntry[]
}

export function SubAgentProgressBlock({
  entries: subAgentEntries
}: SubAgentProgressBlockProps): JSX.Element | null {
  if (subAgentEntries.length === 0) return null

  const allCompleted = subAgentEntries.every((e) => e.isCompleted)
  const totalInput = subAgentEntries.reduce((sum, e) => sum + e.inputTokens, 0)
  const totalOutput = subAgentEntries.reduce((sum, e) => sum + e.outputTokens, 0)

  // ── Collapsed summary (all done) ────────────────────────────────────────
  if (allCompleted) {
    return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          padding: '4px 8px',
          borderRadius: '4px',
          background: 'var(--bg-secondary)',
          color: 'var(--text-secondary)',
          fontSize: '12px',
          marginTop: '2px'
        }}
      >
        <span style={{ color: 'var(--success)', fontSize: '11px' }}>✓</span>
        <span>
          {subAgentEntries.length} SubAgent{subAgentEntries.length > 1 ? 's' : ''} completed
        </span>
        <span style={{ color: 'var(--text-dimmed)', marginLeft: '4px' }}>
          ({formatTokenCount(totalInput)} in / {formatTokenCount(totalOutput)} out)
        </span>
      </div>
    )
  }

  // ── Active table ──────────────────────────────────────────────────────────
  return (
    <div
      style={{
        borderRadius: '4px',
        border: '1px solid var(--border-default)',
        background: 'var(--bg-secondary)',
        overflow: 'hidden',
        marginTop: '2px',
        fontSize: '12px'
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: '16px minmax(100px, 1fr) minmax(120px, 2fr) 80px',
          gap: '8px',
          padding: '4px 8px',
          background: 'var(--bg-tertiary)',
          borderBottom: '1px solid var(--border-default)',
          color: 'var(--text-dimmed)',
          fontSize: '11px',
          userSelect: 'none'
        }}
      >
        <span />
        <span>Agent</span>
        <span>Current tool</span>
        <span style={{ textAlign: 'right' }}>Tokens</span>
      </div>

      {/* Rows */}
      {subAgentEntries.map((entry, idx) => (
        <div
          key={idx}
          style={{
            display: 'grid',
            gridTemplateColumns: '16px minmax(100px, 1fr) minmax(120px, 2fr) 80px',
            gap: '8px',
            padding: '4px 8px',
            alignItems: 'center',
            borderBottom: idx < subAgentEntries.length - 1 ? '1px solid var(--border-default)' : 'none',
            color: 'var(--text-secondary)'
          }}
        >
          {/* Status indicator */}
          <span>
            {entry.isCompleted ? (
              <span style={{ color: 'var(--success)', fontSize: '11px' }}>●</span>
            ) : (
              <span
                className="animate-spin-custom"
                style={{
                  display: 'inline-block',
                  width: '10px',
                  height: '10px',
                  borderRadius: '50%',
                  border: '1.5px solid var(--border-active)',
                  borderTopColor: 'var(--accent)'
                }}
              />
            )}
          </span>
          {/* Label */}
          <span
            style={{
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              color: 'var(--text-primary)'
            }}
          >
            {entry.label}
          </span>
          {/* Current tool */}
          <span
            style={{
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              color: 'var(--text-dimmed)',
              fontFamily: 'var(--font-mono)',
              fontSize: '11px'
            }}
          >
            {entry.currentToolDisplay ?? entry.currentTool ?? '—'}
          </span>
          {/* Token counts */}
          <span style={{ textAlign: 'right', color: 'var(--text-dimmed)', fontFamily: 'var(--font-mono)', fontSize: '11px' }}>
            {formatTokenCount(entry.inputTokens)}/{formatTokenCount(entry.outputTokens)}
          </span>
        </div>
      ))}
    </div>
  )
}
