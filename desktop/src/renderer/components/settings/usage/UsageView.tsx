import { useEffect, useMemo, type JSX } from 'react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { useConnectionStore } from '../../../stores/connectionStore'
import { usePluginStore } from '../../../stores/pluginStore'
import { useUsageStore, USAGE_RANGE_DAYS, type UsageRange } from '../../../stores/usageStore'
import { addLocalDays, localDayKeys } from '../../../utils/localDay'
import { OpenInBrowserIcon } from '../../ui/AppIcons'
import { IconButton } from '../../ui/IconButton'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { SettingsPanelShell } from '../SettingsPanelShell'
import { settingsPlaceholderStyle } from '../settingsTypography'
import { ToolActivityCharts } from './ToolActivityCharts'
import { TopThreadsTable } from './TopThreadsTable'
import { UsageHistoryChart } from './UsageHistoryChart'
import styles from './usage.module.css'

export function UsageView({ dashboardUrl }: { dashboardUrl: string | null }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const capable = useConnectionStore((s) => s.capabilities?.usageTelemetry === true)
  const ranges = useUsageStore((s) => s.ranges)
  const historyGroup = useUsageStore((s) => s.historyGroup)
  const history = useUsageStore((s) => s.history)
  const toolCalls = useUsageStore((s) => s.toolCalls)
  const skillUses = useUsageStore((s) => s.skillUses)
  const threads = useUsageStore((s) => s.threads)
  const setRange = useUsageStore((s) => s.setRange)
  const setHistoryGroup = useUsageStore((s) => s.setHistoryGroup)
  const load = useUsageStore((s) => s.load)
  const plugins = usePluginStore((s) => s.plugins)

  useEffect(() => {
    if (capable) void load()
  }, [capable, load])

  const dayKeys = useMemo((): Record<UsageRange, string[]> => {
    const today = new Date()
    const keysFor = (range: UsageRange): string[] => localDayKeys(addLocalDays(today, -(USAGE_RANGE_DAYS[range] - 1)), today)
    return { '7d': keysFor('7d'), '30d': keysFor('30d') }
  }, [])

  const dateFormat = useMemo(() => new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric' }), [locale])
  const dateTimeFormat = useMemo(
    () => new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }),
    [locale]
  )
  const formatDate = (key: string): string => dateFormat.format(new Date(`${key}T00:00:00`))
  const formatDateTime = (iso: string): string => dateTimeFormat.format(new Date(iso))

  const pluginName = useMemo(() => {
    const names = new Map(plugins.map((plugin) => [plugin.id, plugin.displayName]))
    return (pluginId: string): string | null => names.get(pluginId) ?? null
  }, [plugins])

  return (
    <SettingsPanelShell title={t('settings.tab.usage')} description={t('settings.usage.description')}>
      {!capable ? (
        <SettingsGroup>
          <SettingsRow>
            <div style={settingsPlaceholderStyle()}>{t('settings.usage.unavailable')}</div>
          </SettingsRow>
        </SettingsGroup>
      ) : (
        <div className={styles.sections}>
          <UsageHistoryChart
            state={history}
            group={historyGroup}
            onGroupChange={setHistoryGroup}
            range={ranges.history}
            onRangeChange={(range) => setRange('history', range)}
            dayKeys={dayKeys[ranges.history]}
            formatDate={formatDate}
            pluginName={pluginName}
            onRetry={() => void load('history')}
            t={t}
          />

          <ToolActivityCharts
            toolCalls={toolCalls}
            skillUses={skillUses}
            range={ranges.activity}
            onRangeChange={(range) => setRange('activity', range)}
            dayKeys={dayKeys[ranges.activity]}
            formatDate={formatDate}
            pluginName={pluginName}
            onRetry={() => void load('activity')}
            t={t}
          />

          <TopThreadsTable
            state={threads}
            range={ranges.threads}
            onRangeChange={(range) => setRange('threads', range)}
            formatDateTime={formatDateTime}
            onRetry={() => void load('threads')}
            t={t}
          />
        </div>
      )}

      <div className={styles.footer}>
        <span>{dashboardUrl ?? t('settings.usage.dashboardUnavailable')}</span>
        <IconButton
          icon={<OpenInBrowserIcon size={15} />}
          label={t('settings.openDashboard')}
          tooltipLabel={t('settings.openDashboard')}
          onClick={() => {
            if (dashboardUrl) void window.api.shell.openExternal(dashboardUrl)
          }}
          disabled={!dashboardUrl}
        />
      </div>
    </SettingsPanelShell>
  )
}
