import { useEffect, useId } from 'react'
import { Download, ExternalLink, Sparkles } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { SkillMarketProviderFilter } from '../../stores/skillMarketStore'
import type { MarketSkillDetail } from '../../../shared/skillMarket'
import { stripYamlFrontmatter } from '../../utils/skillMarkdown'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Button } from '../ui/Button'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import { SkillAvatar } from './SkillAvatar'
import styles from './MarketSkillDetailDialog.module.css'

interface MarketSkillDetailDialogProps {
  skill: MarketSkillDetail
  loading: boolean
  installing: boolean
  dotCraftInstalling: boolean
  dotCraftDisabledReason: string | null
  installDisabledReason: string | null
  onClose: () => void
  onInstall: () => void
  onDotCraftInstall: () => void
}

export function MarketSkillDetailDialog({
  skill,
  loading,
  installing,
  dotCraftInstalling,
  dotCraftDisabledReason,
  installDisabledReason,
  onClose,
  onInstall,
  onDotCraftInstall
}: MarketSkillDetailDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()

  useEffect(() => {
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const installLabel = skill.updateAvailable
    ? t('skillMarket.update')
    : skill.installed
      ? t('skillMarket.reinstall')
      : t('skillMarket.install')
  const dotCraftInstallLabel = skill.updateAvailable
    ? t('skillMarket.updateWithDotCraft')
    : skill.installed
      ? t('skillMarket.checkWithDotCraft')
      : t('skillMarket.installWithDotCraft')
  const dotCraftButtonDisabled = Boolean(dotCraftDisabledReason) || dotCraftInstalling || loading || installing
  const installButtonDisabled = Boolean(installDisabledReason) || installing || loading

  return (
    <ModalDialog
      titleId={titleId}
      width={760}
      onDismiss={onClose}
      header={(
        <ModalHeader
          icon={<SkillAvatar size={36} />}
          badgedIcon={false}
          title={skill.name}
          titleId={titleId}
          description={skill.description || skill.slug}
          onClose={onClose}
          closeLabel={t('skillDetail.close')}
        />
      )}
      footer={(
        <ModalFooter>
          <Button
            variant="ghost"
            iconLeft={<ExternalLink size={14} aria-hidden />}
            onClick={() => {
              if (skill.sourceUrl) void window.api.shell.openExternal(skill.sourceUrl)
            }}
            disabled={!skill.sourceUrl}
          >
            {t('skillMarket.openSource')}
          </Button>
          <ActionTooltip
            label={installLabel}
            disabledReason={installDisabledReason ?? undefined}
            placement="top"
          >
            <Button variant="secondary" iconLeft={<Download size={14} aria-hidden />} onClick={onInstall} disabled={installButtonDisabled}>
              {installing ? t('skillMarket.installing') : installLabel}
            </Button>
          </ActionTooltip>
          <ActionTooltip
            label={dotCraftInstallLabel}
            disabledReason={dotCraftDisabledReason ?? undefined}
            placement="top"
          >
            <Button variant="primary" iconLeft={<Sparkles size={14} aria-hidden />} onClick={onDotCraftInstall} disabled={dotCraftButtonDisabled}>
              {dotCraftInstalling ? t('skillMarket.dotCraftInstallPreparing') : dotCraftInstallLabel}
            </Button>
          </ActionTooltip>
        </ModalFooter>
      )}
    >
      <div className={styles.meta}>
        <Meta label={t('skillMarket.provider')} value={providerLabel(skill.provider)} />
        <Meta label={t('skillMarket.version')} value={skill.version || t('skillMarket.versionUnknown')} />
        {skill.author && <Meta label={t('skillMarket.author')} value={skill.author} />}
        {skill.downloads != null && <Meta label={t('skillMarket.downloadsLabel')} value={String(skill.downloads)} />}
      </div>
      <div className={styles.readme}>
        {loading ? (
          <p className={styles.status}>{t('skillDetail.loading')}</p>
        ) : skill.readme ? (
          <MarkdownRenderer content={stripYamlFrontmatter(skill.readme)} linkMode="external" />
        ) : skill.description ? (
          <p className={styles.fallback}>{skill.description}</p>
        ) : (
          <p className={styles.status}>{t('skillMarket.noReadme')}</p>
        )}
      </div>
    </ModalDialog>
  )
}

function Meta({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div className={styles.metaItem}>
      <span className={styles.metaLabel}>{label}</span>
      <span className={styles.metaValue}>{value}</span>
    </div>
  )
}

export function providerLabel(provider: SkillMarketProviderFilter): string {
  if (provider === 'skillhub') return 'SkillHub'
  if (provider === 'clawhub') return 'ClawHub'
  return 'All'
}
