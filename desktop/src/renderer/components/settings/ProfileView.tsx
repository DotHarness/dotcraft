import { useEffect, useState, type CSSProperties, type JSX } from 'react'
import { Pencil } from 'lucide-react'
import type { MessageKey } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'
import { useUsageConnectionStore } from '../../stores/usageConnectionStore'
import { useProfileStore, type ProfileInsightsWire, type UsageDayWire } from '../../stores/profileStore'
import { useUsageRefresh } from './usage/useUsageRefresh'
import { formatCompactCount } from '../../utils/formatCompactCount'
import { ActionTooltip } from '../ui/ActionTooltip'
import { GitHubIcon } from '../ui/AppIcons'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { Skeleton } from '../ui/Skeleton'
import { UserAvatar } from '../ui/UserAvatar'
import { SettingsPageHeader } from './SettingsPageHeader'
import { settingsMetaTextStyle, settingsPlaceholderStyle } from './settingsTypography'
import { TokenActivityHeatmap, type HeatmapMode } from './profile/TokenActivityHeatmap'
import { ActivityInsights } from './profile/ActivityInsights'
import { MostUsedPlugins } from './profile/MostUsedPlugins'
import { ProfileInsightsSkeleton } from './profile/ProfileInsightsSkeleton'
import insightsStyles from './profile/ProfileInsights.module.css'

type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

function openGithubProfile(username: string): void {
  void window.api.shell.openExternal(`https://github.com/${encodeURIComponent(username)}`)
}

export function ProfileView(): JSX.Element {
  const t = useT()
  const capable = useUsageConnectionStore((s) => s.capable !== false)

  const days = useProfileStore((s) => s.days)
  const loading = useProfileStore((s) => s.loading)
  const loadedOnce = useProfileStore((s) => s.loadedOnce)
  const error = useProfileStore((s) => s.error)
  const githubUsername = useProfileStore((s) => s.githubUsername)
  const fetchHistory = useProfileStore((s) => s.fetchHistory)
  const loadIdentity = useProfileStore((s) => s.loadIdentity)

  const insights = useProfileStore((s) => s.insights)
  const insightsLoading = useProfileStore((s) => s.insightsLoading)
  const insightsLoadedOnce = useProfileStore((s) => s.insightsLoadedOnce)
  const fetchInsights = useProfileStore((s) => s.fetchInsights)
  const insightsError = useProfileStore((s) => s.insightsError)
  const load = useProfileStore((s) => s.load)

  const [mode, setMode] = useState<HeatmapMode>('daily')
  const [editing, setEditing] = useState(false)

  useEffect(() => {
    void loadIdentity()
  }, [loadIdentity])

  useUsageRefresh(load)

  const editAction = editing ? undefined : (
    <Button variant="ghost" iconLeft={githubUsername ? <Pencil size={14} /> : <GitHubIcon size={14} />} onClick={() => setEditing(true)}>
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

        {capable && insightsLoadedOnce && insights && <StatStrip insights={insights} t={t} />}
        {capable && !insightsLoadedOnce && insightsLoading && <StatStripSkeleton />}
        {capable && !insightsLoadedOnce && insightsError && (
          <div className={insightsStyles.error} role="alert">
            <span>{t('settings.usage.loadError')}</span>
            <Button variant="secondary" onClick={() => void fetchInsights()}>{t('settings.usage.retry')}</Button>
          </div>
        )}

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
            onRetry={() => void fetchHistory()}
            t={t}
          />
        </section>

        {capable && insightsLoadedOnce && insights && (
          <div className={insightsStyles.grid}>
            <ActivityInsights insights={insights} t={t} />
            <MostUsedPlugins skills={insights.skills} t={t} />
          </div>
        )}
        {capable && !insightsLoadedOnce && insightsLoading && <ProfileInsightsSkeleton t={t} />}
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
        <Button variant="secondary" onClick={onRetry}>
          {t('settings.usage.retry')}
        </Button>
      </div>
    )
  }
  return <TokenActivityHeatmap days={days} mode={mode} />
}

/**
 * Mirrors the contribution grid's 53×7 shape so the layout doesn't shift when the
 * real heatmap arrives.
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
      <UserAvatar name={displayName} avatarUrl={avatarUrl} size={72} />

      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '6px' }}>
        <span style={{ fontSize: '20px', fontWeight: 600, color: 'var(--text-primary)' }}>
          {displayName}
        </span>

        {editing ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>github.com/</span>
            <Input
              autoFocus
              value={draft}
              placeholder={t('settings.profile.usernamePlaceholder')}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') commit()
                if (e.key === 'Escape') onClose()
              }}
              frameless
              style={{ height: '42px', padding: '0 12px', background: 'var(--bg-tertiary)' }}
            />
            <Button variant="primary" onClick={commit}>
              {t('settings.profile.save')}
            </Button>
            <Button variant="ghost" onClick={onClose}>
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

function StatStrip({ insights, t }: { insights: ProfileInsightsWire; t: TFn }): JSX.Element {
  const cards: Array<{ label: MessageKey; value: string; full?: string }> = [
    {
      label: 'settings.profile.stat.lifetime',
      value: formatCompactCount(insights.lifetimeTokens),
      full: insights.lifetimeTokens.toLocaleString()
    },
    {
      label: 'settings.profile.stat.peak',
      value: formatCompactCount(insights.peakDailyTokens),
      full: insights.peakDailyTokens.toLocaleString()
    },
    {
      label: 'settings.profile.stat.longestTask',
      value: formatDuration(insights.longestTaskMs)
    },
    {
      label: 'settings.profile.stat.currentStreak',
      value: t('settings.profile.stat.days', { count: insights.currentStreakDays })
    },
    {
      label: 'settings.profile.stat.longestStreak',
      value: t('settings.profile.stat.days', { count: insights.longestStreakDays })
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

const dimmedTextStyle: CSSProperties = settingsPlaceholderStyle()

const handleLinkStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  padding: 0,
  fontSize: '13px',
  color: 'var(--text-secondary)',
  cursor: 'pointer'
}
