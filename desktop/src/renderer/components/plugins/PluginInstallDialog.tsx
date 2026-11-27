import { useEffect, useRef, useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { PluginEntry } from '../../stores/pluginStore'
import { addToast } from '../../stores/toastStore'
import { PluginIcon, pluginSubtitle, pluginTitle } from './PluginCatalogItem'
import { getPluginContentSummaries } from '../../utils/pluginContentSummaries'
import { Button } from '../ui/Button'
import { DialogCloseButton } from '../ui/DialogCloseButton'
import { IdentityMark } from '../ui/IdentityMark'
import { ModalDialog } from '../ui/ModalDialog'
import { PluginContentIcon } from './PluginContentIcon'
import styles from './PluginInstallDialog.module.css'

type SetupStage = 'pluginInstall' | 'trustRequired' | 'complete'

const dotharLogoUrl = new URL('../../assets/brand/dothar.svg', import.meta.url).href

export function PluginInstallDialog({
  plugin,
  installing,
  confirmLabel,
  onInstall,
  onTrust,
  onSetupComplete,
  onClose
}: {
  plugin: PluginEntry
  installing?: boolean
  /** Replaces the setup steps with one action that answers a confirmation request through `onInstall`. */
  confirmLabel?: string
  onInstall: () => Promise<void> | void
  onTrust?: () => Promise<void> | void
  onSetupComplete?: () => void
  onClose: () => void
}): JSX.Element {
  const t = useT()
  const title = pluginTitle(plugin)
  const capabilities = plugin.interface?.capabilities ?? []
  const [busyKey, setBusyKey] = useState<string | null>(null)

  const needsTrust = plugin.dotnet != null && plugin.dotnetRuntime?.trustStatus !== 'trusted'

  const setupStage: SetupStage = !plugin.installed
    ? 'pluginInstall'
    : needsTrust
      // Copying the bundle is not authority: an in-process plugin stays blocked until this step.
      ? 'trustRequired'
      : 'complete'

  const setupFinished = setupStage === 'complete'
  const completionReported = useRef(false)
  useEffect(() => {
    if (confirmLabel || !setupFinished || completionReported.current) return
    completionReported.current = true
    ;(onSetupComplete ?? onClose)()
  }, [confirmLabel, onClose, onSetupComplete, setupFinished])

  const dialogTitle = plugin.installed && (confirmLabel || needsTrust)
    ? t('plugins.installDialog.setupTitle', { name: title })
    : t('plugins.installDialog.title', { name: title })

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  async function run(key: string, action: () => Promise<void>): Promise<void> {
    if (busyKey) return
    setBusyKey(key)
    try {
      await action()
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      setBusyKey(null)
    }
  }

  return (
    <ModalDialog
      titleId="plugin-install-title"
      width={600}
      onDismiss={onClose}
      header={(
        <div className={styles.hero}>
          <span className={styles.close}>
            <DialogCloseButton label={t('common.close')} onClose={onClose} />
          </span>
          <div className={styles.logos}>
            <IdentityMark role="hero" size={56} src={dotharLogoUrl} fallback="D" />
            <span className={styles.dotTrail}>•••</span>
            <PluginIcon plugin={plugin} role="hero" size={56} />
          </div>
          <h2 id="plugin-install-title" className={styles.title}>{dialogTitle}</h2>
          <div className={styles.subtitle}>{t('plugins.developedBy', { developer: plugin.interface?.developerName || 'DotHarness' })}</div>
        </div>
      )}
      footer={(
        <div className={styles.actions}>
          {confirmLabel ? (
            <>
              {needsTrust && <TrustNotice name={title} />}
              <Button size="prominent" variant="primary" onClick={() => { void onInstall() }} disabled={installing} loading={installing} className={styles.wide}>
                {confirmLabel}
              </Button>
            </>
          ) : setupStage === 'trustRequired' ? (
            <>
              <TrustNotice name={title} />
              <Button
                size="prominent"
                variant="primary"
                onClick={onTrust ? () => { void run('plugin:trust', async () => { await onTrust() }) } : undefined}
                disabled={!onTrust || busyKey === 'plugin:trust'}
                loading={busyKey === 'plugin:trust'}
                className={styles.wide}
              >
                {t('plugins.installDialog.trustAction')}
              </Button>
            </>
          ) : setupStage === 'pluginInstall' ? (
            <Button size="prominent" variant="primary" onClick={() => { void onInstall() }} disabled={installing} loading={installing} className={styles.wide}>
              {installing ? t('plugins.installing') : t('plugins.installDialog.addToDotCraft')}
            </Button>
          ) : null}
        </div>
      )}
    >
      <div className={styles.infoCard}>
        <div className={styles.cardTitle}>
          <strong>{title}</strong>
        </div>
        <div className={styles.muted}>{t('plugins.providedBy', { developer: plugin.interface?.developerName || 'DotHarness' })}</div>
        <div className={styles.muted}>{t('plugins.detail.category')}: {plugin.interface?.category || 'Coding'}</div>
        <Divider />
        <SectionTitle>{t('plugins.detail.about')}</SectionTitle>
        <p className={styles.description}>{plugin.interface?.longDescription || pluginSubtitle(plugin)}</p>
        <Divider />
        <SectionTitle>{t('plugins.detail.contents')}</SectionTitle>
        <ContentChips plugin={plugin} />
        {capabilities.length > 0 && (
          <>
            <Divider />
            <SectionTitle>{t('plugins.detail.capabilities')}</SectionTitle>
            <div className={styles.chips}>
              {capabilities.map((capability) => <span key={capability} className={styles.chip}>{capability}</span>)}
            </div>
          </>
        )}
      </div>
    </ModalDialog>
  )
}

function ContentChips({ plugin }: { plugin: PluginEntry }): JSX.Element {
  const t = useT()
  const contents = getPluginContentSummaries(plugin, t)
  if (contents.length === 0) {
    return (
      <div className={styles.chips}>
        <span className={styles.muted}>{t('plugins.detail.noContents')}</span>
      </div>
    )
  }

  return (
    <div className={styles.chips}>
      {contents.map((item) => (
        <span key={item.key} className={styles.chip}>
          <PluginContentIcon type={item.type} size={12} />
          <span>{item.type === 'desktopPlugin' || item.type === 'dotnet' || item.type === 'hooks' ? `${item.title} · ${item.kind}` : item.title}</span>
        </span>
      ))}
    </div>
  )
}

function TrustNotice({ name }: { name: string }): JSX.Element {
  const t = useT()
  return (
    <div className={styles.setupPanel} data-tone="warning">
      <span className={styles.setupIcon}>
        <AlertTriangle size={13} aria-hidden />
      </span>
      <span className={styles.stepBody}>
        <strong className={styles.stepTitle}>{t('plugins.installDialog.trustRequired')}</strong>
        <span className={styles.muted}>{t('plugins.installDialog.trustStep', { name })}</span>
      </span>
    </div>
  )
}

function Divider(): JSX.Element {
  return <div className={styles.divider} />
}

function SectionTitle({ children }: { children: string }): JSX.Element {
  return <div className={styles.sectionTitle}>{children}</div>
}
