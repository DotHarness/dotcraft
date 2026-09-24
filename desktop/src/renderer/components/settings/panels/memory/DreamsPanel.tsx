import { Archive } from 'lucide-react'
import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { RefreshIcon } from '../../../ui/AppIcons'
import { Button } from '../../../ui/Button'
import { IconButton } from '../../../ui/IconButton'
import { PillSwitch } from '../../../ui/PillSwitch'
import { Skeleton } from '../../../ui/Skeleton'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsDescriptionWithLearnMore } from '../../SettingsLearnMoreLink'
import { SettingsPanelShell } from '../../SettingsPanelShell'
import { settingsPlaceholderStyle } from '../../settingsTypography'
import { SettingsSelect } from '../../ui/SettingsSelect'
import { formatDreamsIntervalOption } from './dreamsModel'
import type { DreamsSettings } from './useDreamsSettings'

interface DreamsPanelProps {
  dreams: DreamsSettings
  memoryEnabled: boolean
  dashboardUrl: string | null | undefined
  locale: string
  onBack: () => void
}

export function DreamsPanel({ dreams, memoryEnabled, dashboardUrl, locale, onBack }: DreamsPanelProps): JSX.Element {
  const t = useT()
  const settingsDisabled = dreams.settingsBusy || !memoryEnabled

  return (
    <SettingsPanelShell
      title={t('settings.dreams.title')}
      description={
        <SettingsDescriptionWithLearnMore topic="memory" aboutKey="settings.dreams.title">
          {t('settings.dreams.description')}
        </SettingsDescriptionWithLearnMore>
      }
      breadcrumb={
        <SettingsBreadcrumb
          parentLabel={t('settings.tab.personalization')}
          currentLabel={t('settings.dreams.title')}
          onBack={onBack}
        />
      }
      action={
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <IconButton
            icon={<RefreshIcon size={15} />}
            label={t('settings.dreams.refresh')}
            tooltipLabel={t('settings.dreams.refresh')}
            disabled={dreams.runsLoading}
            onClick={() => void dreams.reloadRuns()}
          />
          <Button
            variant="primary"
            disabled={dreams.running || !dreams.enabled || !memoryEnabled}
            onClick={() => void dreams.runNow()}
          >
            {dreams.running
              ? t('settings.personalization.dreamsRunning')
              : t('settings.personalization.dreamsRunNow')}
          </Button>
        </div>
      }
    >
      <SettingsGroup>
        <SettingsRow
          label={t('settings.personalization.dreamsAutoApply')}
          description={t('settings.personalization.dreamsAutoApplyHint')}
          control={
            <PillSwitch
              checked={dreams.autoApply}
              disabled={settingsDisabled}
              aria-label={t('settings.personalization.dreamsAutoApply')}
              onChange={(checked) => {
                void dreams.toggleAutoApply(checked)
              }}
            />
          }
        />
        <SettingsRow
          label={t('settings.personalization.dreamsInterval')}
          description={t('settings.personalization.dreamsIntervalHint')}
          control={
            <SettingsSelect
              ariaLabel={t('settings.personalization.dreamsInterval')}
              value={dreams.interval}
              disabled={settingsDisabled}
              onValueChange={(next) => {
                void dreams.changeInterval(next)
              }}
              style={{ minWidth: '140px' }}
              options={dreams.intervalOptions.map((optionValue) => ({
                value: optionValue,
                label: formatDreamsIntervalOption(optionValue, t)
              }))}
            />
          }
        />
        <SettingsRow
          label={t('settings.personalization.dreamsThreadLookback')}
          description={t('settings.personalization.dreamsThreadLookbackHint')}
          control={
            <SettingsSelect
              ariaLabel={t('settings.personalization.dreamsThreadLookback')}
              value={String(dreams.threadLookbackCount)}
              disabled={settingsDisabled}
              onValueChange={(next) => {
                void dreams.changeThreadLookback(Number(next))
              }}
              style={{ minWidth: '120px' }}
              options={dreams.threadLookbackOptions.map((optionValue) => ({
                value: String(optionValue),
                label: optionValue
              }))}
            />
          }
        />
      </SettingsGroup>
      <SettingsGroup
        title={t('settings.dreams.runs')}
        headerAction={
          <Button
            variant="danger"
            disabled={dreams.archiveAllDisabled}
            onClick={() => void dreams.archiveAll()}
          >
            {t('settings.dreams.archiveAll')}
          </Button>
        }
      >
        {dreams.runsLoading && dreams.runs.length === 0 &&
          ['58%', '44%', '64%'].map((labelWidth, index) => (
            <SettingsRow
              key={`dream-run-skeleton-${index}`}
              label={
                <span
                  role={index === 0 ? 'status' : undefined}
                  aria-label={index === 0 ? t('settings.dreams.loading') : undefined}
                >
                  <Skeleton width={labelWidth} height={13} />
                </span>
              }
              description={<Skeleton width="34%" height={11} />}
              control={<Skeleton width={99} height={32} radius={8} />}
            />
          ))}

        {!dreams.runsLoading && dreams.runs.length === 0 && (
          <SettingsRow>
            <div style={settingsPlaceholderStyle()}>{t('settings.dreams.empty')}</div>
          </SettingsRow>
        )}

        {!dreams.runsLoading && dreams.runs.map((run) => {
          const runTime = run.endedAt ?? run.startedAt
          const statusColor = run.status === 'succeeded'
            ? 'var(--success)'
            : run.status === 'failed'
              ? 'var(--error)'
              : run.status === 'running'
                ? 'var(--info)'
                : 'var(--text-secondary)'
          const running = run.status === 'running'
          return (
            <SettingsRow
              key={run.id}
              label={
                <span style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {runTime
                    ? new Date(runTime).toLocaleString(locale)
                    : t('settings.personalization.dreamsStatus.unknownTime')}
                </span>
              }
              description={
                <span style={{ display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', gap: '6px' }}>
                  <span style={{ color: statusColor }}>
                    {t(`settings.personalization.dreamsStatus.${run.status}`)}
                  </span>
                  <span aria-hidden>·</span>
                  <span>{t('settings.dreams.threadCount', { count: run.processedThreadCount })}</span>
                </span>
              }
              control={
                <div style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                  <ActionTooltip
                    label={dashboardUrl ? t('settings.dreams.openReview') : t('settings.dreams.dashboardUnavailable')}
                  >
                    <Button
                      disabled={!dashboardUrl || running}
                      onClick={() => void dreams.openReview(run.id)}
                    >
                      {t('settings.dreams.openReview')}
                    </Button>
                  </ActionTooltip>
                  <IconButton
                    icon={<Archive size={14} aria-hidden />}
                    label={t('settings.dreams.archive')}
                    tooltipLabel={t('settings.dreams.archive')}
                    disabled={running || dreams.archiveBusy}
                    onClick={() => void dreams.archiveRun(run)}
                  />
                </div>
              }
            />
          )
        })}
      </SettingsGroup>
    </SettingsPanelShell>
  )
}
