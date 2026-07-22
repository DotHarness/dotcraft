import { useEffect, useMemo, useState, type CSSProperties, type JSX } from 'react'
import { Pencil } from 'lucide-react'
import type { MessageKey } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useProfileStore, type UsageDayWire } from '../../stores/profileStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Button } from '../ui/Button'
import { Skeleton } from '../ui/Skeleton'
import { SettingsPageHeader } from './SettingsPageHeader'
import { settingsMetaTextStyle, settingsPlaceholderStyle } from './settingsTypography'
import { TokenActivityHeatmap, type HeatmapMode } from './profile/TokenActivityHeatmap'
import { ActivityInsights } from './profile/ActivityInsights'
import { MostUsedSkills } from './profile/MostUsedSkills'

type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

function openGithubProfile(username: string): void {
  void window.api.shell.openExternal(`https://github.com/${encodeURIComponent(username)}`)
}

/**
 * Settings → Profile: a GitHub-contribution-style view of token usage for the
 * current workspace, plus a lightweight identity header sourced from a public
 * GitHub account. Gated behind the `usageTelemetry` capability (spec §27A).
 *
 * Layout is top-aligned and borderless (one faint stat strip only); the content
 * column is constrained to the heatmap width so the activity tabs and the page
 * Edit action line up with the grid's right edge.
 */
export function ProfileView(): JSX.Element {
  const t = useT()
  const capable = useConnectionStore((s) => s.capabilities?.usageTelemetry === true)

  const days = useProfileStore((s) => s.days)
  const longestTaskMs = useProfileStore((s) => s.longestTaskMs)
  const loading = useProfileStore((s) => s.loading)
  const loadedOnce = useProfileStore((s) => s.loadedOnce)
  const error = useProfileStore((s) => s.error)
  const githubUsername = useProfileStore((s) => s.githubUsername)
  const fetchTimeseries = useProfileStore((s) => s.fetchTimeseries)
  const loadIdentity = useProfileStore((s) => s.loadIdentity)

  const insights = useProfileStore((s) => s.insights)
  const insightsLoadedOnce = useProfileStore((s) => s.insightsLoadedOnce)
  const fetchInsights = useProfileStore((s) => s.fetchInsights)

  const [mode, setMode] = useState<HeatmapMode>('daily')
  const [editing, setEditing] = useState(false)

  useEffect(() => {
    void loadIdentity()
  }, [loadIdentity])

  useEffect(() => {
    if (capable) {
      void fetchTimeseries()
      void fetchInsights()
    }
  }, [capable, fetchTimeseries, fetchInsights])

  const editAction = editing ? undefined : (
    <Button variant="ghost" iconLeft={<Pencil size={14} />} onClick={() => setEditing(true)}>
      {githubUsername ? t('settings.profile.edit') : t('settings.profile.linkGithub')}
    </Button>
  )

  return (
    <div>
      <SettingsPageHeader
        title={t('settings.tab.profile')}
        description={t('settings.profile.description')}
        action={editAction}
      />

      <div style={{ display: 'flex', flexDirection: 'column', gap: '28px', marginTop: '48px' }}>
        <ProfileHeader editing={editing} onClose={() => setEditing(false)} t={t} />

        {capable && loadedOnce && <StatStrip days={days} longestTaskMs={longestTaskMs} t={t} />}
        {capable && !loadedOnce && loading && <StatStripSkeleton />}

        <section style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
          <header style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
            <div style={{ flex: 1, minWidth: 0, fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
              {t('settings.profile.heatmap.title')}
            </div>
            <ModeTabs mode={mode} onChange={setMode} t={t} />
          </header>

          <ActivityBody
            capable={capable}
            loading={loading}
            loadedOnce={loadedOnce}
            error={error}
            days={days}
            mode={mode}
            onRetry={() => void fetchTimeseries()}
            t={t}
          />
        </section>

        {capable && insightsLoadedOnce && insights && (
          <section style={insightsGridStyle}>
            <ActivityInsights insights={insights} t={t} />
            <MostUsedSkills skills={insights.skills} t={t} />
          </section>
        )}
      </div>
    </div>
  )
}

function ActivityBody({
  capable,
  loading,
  loadedOnce,
  error,
  days,
  mode,
  onRetry,
  t
}: {
  capable: boolean
  loading: boolean
  loadedOnce: boolean
  error: string | null
  days: UsageDayWire[]
  mode: HeatmapMode
  onRetry: () => void
  t: TFn
}): JSX.Element {
  if (!capable) {
    return <div style={dimmedTextStyle}>{t('settings.usage.unavailable')}</div>
  }
  if (loading && !loadedOnce) {
    return <ActivitySkeleton label={t('settings.usage.refresh')} />
  }
  if (error && !loadedOnce) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
        <div style={{ ...dimmedTextStyle, color: 'var(--error)' }}>{t('settings.usage.loadError')}</div>
        <Button variant="secondary" size="sm" onClick={onRetry}>
          {t('settings.usage.retry')}
        </Button>
      </div>
    )
  }
  return <TokenActivityHeatmap days={days} mode={mode} />
}

/**
 * Loading placeholder for the activity heatmap. Mirrors the contribution grid's
 * shape (53 week columns × 7 day rows) so the layout doesn't shift when the real
 * heatmap arrives. Scales to fit like the real SVG; no spinner.
 */
function ActivitySkeleton({ label }: { label: string }): JSX.Element {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label={label}
      style={{ overflowX: 'auto', paddingBottom: '2px' }}
    >
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(53, minmax(0, 1fr))',
          gridTemplateRows: 'repeat(7, 11px)',
          gap: '3px',
          width: '100%'
        }}
      >
        {Array.from({ length: 53 * 7 }, (_, index) => (
          <Skeleton key={index} height={11} radius={2} />
        ))}
      </div>
    </div>
  )
}

/** Placeholder for the stat strip while the first usage payload loads, so the
 * strip's space is held instead of appearing and shifting content down. */
function StatStripSkeleton(): JSX.Element {
  return (
    <div
      aria-hidden="true"
      style={{
        display: 'flex',
        border: '1px solid var(--border-default)',
        borderRadius: '10px',
        padding: '10px 8px'
      }}
    >
      {Array.from({ length: 5 }, (_, index) => (
        <div
          key={index}
          style={{
            flex: 1,
            minWidth: 0,
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: '6px'
          }}
        >
          <Skeleton width={46} height={18} />
          <Skeleton width={62} height={10} />
        </div>
      ))}
    </div>
  )
}

// ── Identity header (centered, borderless) ───────────────────────────────────

function ProfileHeader({
  editing,
  onClose,
  t
}: {
  editing: boolean
  onClose: () => void
  t: TFn
}): JSX.Element {
  const githubUsername = useProfileStore((s) => s.githubUsername)
  const githubProfile = useProfileStore((s) => s.githubProfile)
  const setGithubUsername = useProfileStore((s) => s.setGithubUsername)

  const [draft, setDraft] = useState('')

  // Seed the draft whenever the editor opens.
  useEffect(() => {
    if (editing) setDraft(githubUsername ?? '')
  }, [editing, githubUsername])

  const displayName =
    githubProfile?.name ?? (githubUsername ? `@${githubUsername}` : t('settings.profile.anonymous'))
  const handle = githubUsername ? `@${githubUsername}` : null
  const avatarUrl = githubProfile?.avatarUrl ?? null

  const commit = (): void => {
    void setGithubUsername(draft.trim() === '' ? null : draft.trim())
    onClose()
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: '12px',
        textAlign: 'center'
      }}
    >
      <Avatar name={displayName} avatarUrl={avatarUrl} />

      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '6px' }}>
        <span style={{ fontSize: '20px', fontWeight: 600, color: 'var(--text-primary)' }}>
          {displayName}
        </span>

        {editing ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>github.com/</span>
            <input
              autoFocus
              value={draft}
              placeholder={t('settings.profile.usernamePlaceholder')}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') commit()
                if (e.key === 'Escape') onClose()
              }}
              style={inputStyle}
            />
            <Button variant="primary" size="sm" onClick={commit}>
              {t('settings.profile.save')}
            </Button>
            <Button variant="ghost" size="sm" onClick={onClose}>
              {t('common.cancel')}
            </Button>
          </div>
        ) : handle && githubUsername ? (
          <ActionTooltip label={`github.com/${githubUsername}`} placement="bottom">
            <button
              type="button"
              onClick={() => openGithubProfile(githubUsername)}
              style={handleLinkStyle}
            >
              {handle}
            </button>
          </ActionTooltip>
        ) : (
          <span style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>
            {t('settings.profile.linkPrompt')}
          </span>
        )}
      </div>
    </div>
  )
}

function Avatar({ name, avatarUrl }: { name: string; avatarUrl: string | null }): JSX.Element {
  const [failed, setFailed] = useState(false)
  const initials = useMemo(() => deriveInitials(name), [name])

  if (avatarUrl && !failed) {
    return (
      <img
        src={avatarUrl}
        alt={name}
        width={72}
        height={72}
        onError={() => setFailed(true)}
        style={{ width: '72px', height: '72px', borderRadius: '50%', objectFit: 'cover', flexShrink: 0 }}
      />
    )
  }
  return (
    <div
      aria-hidden
      style={{
        width: '72px',
        height: '72px',
        borderRadius: '50%',
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--accent)',
        color: '#fff',
        fontSize: '24px',
        fontWeight: 600
      }}
    >
      {initials}
    </div>
  )
}

// ── Stat strip (single faint container, evenly spaced, centered) ─────────────

function StatStrip({
  days,
  longestTaskMs,
  t
}: {
  days: UsageDayWire[]
  longestTaskMs: number
  t: TFn
}): JSX.Element {
  const stats = useMemo(() => computeStats(days), [days])
  const cards: Array<{ label: MessageKey; value: string; full?: string }> = [
    {
      label: 'settings.profile.stat.lifetime',
      value: formatCompact(stats.lifetimeTokens),
      full: stats.lifetimeTokens.toLocaleString()
    },
    {
      label: 'settings.profile.stat.peak',
      value: formatCompact(stats.peakTokens),
      full: stats.peakTokens.toLocaleString()
    },
    {
      label: 'settings.profile.stat.longestTask',
      value: formatDuration(longestTaskMs)
    },
    {
      label: 'settings.profile.stat.currentStreak',
      value: t('settings.profile.stat.days', { count: stats.currentStreak })
    },
    {
      label: 'settings.profile.stat.longestStreak',
      value: t('settings.profile.stat.days', { count: stats.longestStreak })
    }
  ]

  return (
    <div
      style={{
        display: 'flex',
        border: '1px solid var(--border-default)',
        borderRadius: '10px',
        padding: '10px 8px'
      }}
    >
      {cards.map((card) => (
        <div
          key={card.label}
          style={{
            flex: 1,
            minWidth: 0,
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: '1px',
            textAlign: 'center'
          }}
        >
          {card.full ? (
            <ActionTooltip
              label={card.full}
              wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
            >
              <div
                style={{ fontSize: '17px', fontWeight: 600, lineHeight: 1.2, color: 'var(--text-primary)', display: 'block' }}
              >
                {card.value}
              </div>
            </ActionTooltip>
          ) : (
            <div
              style={{ fontSize: '17px', fontWeight: 600, lineHeight: 1.2, color: 'var(--text-primary)' }}
            >
              {card.value}
            </div>
          )}
          <div style={settingsMetaTextStyle()}>{t(card.label)}</div>
        </div>
      ))}
    </div>
  )
}

// ── View-mode tabs (plain text, no boxed segmented control) ──────────────────

function ModeTabs({
  mode,
  onChange,
  t
}: {
  mode: HeatmapMode
  onChange: (mode: HeatmapMode) => void
  t: TFn
}): JSX.Element {
  const options: Array<{ id: HeatmapMode; label: MessageKey }> = [
    { id: 'daily', label: 'settings.profile.mode.daily' },
    { id: 'weekly', label: 'settings.profile.mode.weekly' },
    { id: 'cumulative', label: 'settings.profile.mode.cumulative' }
  ]
  return (
    <div role="tablist" style={{ display: 'inline-flex', alignItems: 'center', gap: '14px' }}>
      {options.map((opt) => {
        const active = opt.id === mode
        return (
          <button
            key={opt.id}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(opt.id)}
            style={{
              border: 'none',
              background: 'transparent',
              padding: 0,
              fontSize: '13px',
              fontWeight: active ? 600 : 500,
              cursor: 'pointer',
              color: active ? 'var(--text-primary)' : 'var(--text-dimmed)'
            }}
          >
            {t(opt.label)}
          </button>
        )
      })}
    </div>
  )
}

// ── Derived metrics ───────────────────────────────────────────────────────────

interface ProfileStats {
  lifetimeTokens: number
  peakTokens: number
  currentStreak: number
  longestStreak: number
}

function computeStats(days: UsageDayWire[]): ProfileStats {
  let lifetime = 0
  let peak = 0
  const active = new Set<string>()
  for (const d of days) {
    lifetime += d.totalTokens
    if (d.totalTokens > peak) peak = d.totalTokens
    if (d.totalTokens > 0) active.add(d.date)
  }

  return {
    lifetimeTokens: lifetime,
    peakTokens: peak,
    currentStreak: computeCurrentStreak(active),
    longestStreak: computeLongestStreak(active)
  }
}

function dayKey(d: Date): string {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function computeCurrentStreak(active: Set<string>): number {
  if (active.size === 0) return 0
  const cursor = new Date()
  cursor.setHours(0, 0, 0, 0)
  // Allow the streak to count even if today has no usage yet (start from yesterday).
  if (!active.has(dayKey(cursor))) {
    cursor.setDate(cursor.getDate() - 1)
    if (!active.has(dayKey(cursor))) return 0
  }
  let streak = 0
  while (active.has(dayKey(cursor))) {
    streak++
    cursor.setDate(cursor.getDate() - 1)
  }
  return streak
}

function computeLongestStreak(active: Set<string>): number {
  if (active.size === 0) return 0
  const sorted = [...active].sort()
  let longest = 1
  let run = 1
  for (let i = 1; i < sorted.length; i++) {
    const prev = new Date(`${sorted[i - 1]}T00:00:00`)
    const curr = new Date(`${sorted[i]}T00:00:00`)
    const diffDays = Math.round((curr.getTime() - prev.getTime()) / 86_400_000)
    if (diffDays === 1) {
      run++
      longest = Math.max(longest, run)
    } else {
      run = 1
    }
  }
  return longest
}

function deriveInitials(name: string): string {
  const cleaned = name.replace(/^@/, '').trim()
  if (!cleaned) return '?'
  const parts = cleaned.split(/[\s_-]+/).filter(Boolean)
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase()
  return cleaned.slice(0, 2).toUpperCase()
}

function formatCompact(n: number): string {
  if (!Number.isFinite(n)) return '0'
  const abs = Math.abs(n)
  if (abs < 1000) return String(Math.round(n))
  if (abs < 1_000_000) return `${(n / 1000).toFixed(1)}k`
  if (abs < 1_000_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  return `${(n / 1_000_000_000).toFixed(1)}B`
}

/** Compact duration: "2h 10m" / "10m" / "45s"; em-dash when none. */
function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return '—'
  const totalSeconds = Math.round(ms / 1000)
  if (totalSeconds < 60) return `${totalSeconds}s`
  const totalMinutes = Math.floor(totalSeconds / 60)
  if (totalMinutes < 60) return `${totalMinutes}m`
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`
}

const insightsGridStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '1fr 1fr',
  gap: '40px',
  alignItems: 'start'
}

const dimmedTextStyle: CSSProperties = settingsPlaceholderStyle()

const handleLinkStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  padding: 0,
  fontSize: '13px',
  color: 'var(--text-secondary)',
  cursor: 'pointer'
}

const inputStyle: CSSProperties = {
  border: '1px solid var(--border-default)',
  background: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  borderRadius: '8px',
  padding: '4px 8px',
  fontSize: '13px',
  minWidth: '160px'
}
