import { useState, type ReactNode } from 'react'
import { Settings } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { CatalogBreadcrumb, CatalogTopBar } from '../catalog/CatalogSurface'
import { IconButton } from '../ui/IconButton'
import { PluginInstallButton } from '../plugins/PluginInstallButton'
import { ChannelFormPage } from './ChannelFormPage'
import styles from './ChannelModuleDetailPage.module.css'

export type ChannelModuleDetailMode = 'preview' | 'manage'

export interface ChannelDetailInfoItem {
  label: string
  value: string
}

interface ChannelModuleDetailPageProps {
  title: string
  subtitle: string
  logoPath?: string
  previewPrompt: string
  description: string
  infoItems: ChannelDetailInfoItem[]
  active: boolean
  busy: boolean
  controlsAvailable: boolean
  initialMode?: ChannelModuleDetailMode
  onBack: () => void
  onToggleConnection: () => void
  renderManage: (actions: { onCancel: () => void; onSaved: () => void }) => ReactNode
}

export function ChannelModuleDetailPage({
  title,
  subtitle,
  logoPath,
  previewPrompt,
  description,
  infoItems,
  active,
  busy,
  controlsAvailable,
  initialMode = 'preview',
  onBack,
  onToggleConnection,
  renderManage,
}: ChannelModuleDetailPageProps): JSX.Element {
  const t = useT()
  const [mode, setMode] = useState<ChannelModuleDetailMode>(initialMode)

  if (mode === 'manage') {
    const showPreview = (): void => setMode('preview')
    return (
      <ChannelFormPage
        trail={[{ label: title, onClick: showPreview }]}
        breadcrumbLabel={t('plugins.manage')}
        title={t('channels.detail.manageTitle', { name: title })}
        description={t('channels.detail.manageDescription')}
        onBack={onBack}
      >
        {renderManage({ onCancel: showPreview, onSaved: showPreview })}
      </ChannelFormPage>
    )
  }

  return (
    <div className={styles.page}>
      <CatalogTopBar
        navigation={(
          <CatalogBreadcrumb
            parentLabel={t('channels.title')}
            currentLabel={title}
            onBack={onBack}
          />
        )}
      />
      <main className={`${styles.scroll} dc-scrollbar-stable`}>
        <div className={styles.content}>
          <header className={styles.header}>
            <div className={styles.iconRow}>
              <ChannelDetailIcon logoPath={logoPath} title={title} className={styles.heroIcon} />
              <div className={styles.actions}>
                <IconButton
                  icon={<Settings size={16} aria-hidden />}
                  label={t('plugins.manage')}
                  tooltipLabel={t('plugins.manage')}
                  onClick={() => setMode('manage')}
                />
                <PluginInstallButton
                  variant={active ? 'secondary' : 'primary'}
                  loading={busy}
                  disabled={!controlsAvailable}
                  onClick={onToggleConnection}
                >
                  {t(active ? 'appBinding.disconnect' : 'appBinding.connect')}
                </PluginInstallButton>
              </div>
            </div>
            <h1>{title}</h1>
            <p>{subtitle}</p>
          </header>

          <div className={styles.preview} aria-label={t('channels.detail.preview')}>
            <div className={styles.prompt}>
              <span className={styles.promptPrefix}>
                <ChannelDetailIcon logoPath={logoPath} title={title} className={styles.promptIcon} />
                <strong>{title}</strong>
              </span>
              <span className={styles.promptText}>{previewPrompt}</span>
            </div>
          </div>

          <p className={styles.description}>{description}</p>

          <section className={styles.information}>
            <h2>{t('channels.detail.info')}</h2>
            <dl>
              {infoItems.map((item) => (
                <div key={item.label}>
                  <dt>{item.label}</dt>
                  <dd>{item.value}</dd>
                </div>
              ))}
            </dl>
          </section>
        </div>
      </main>
    </div>
  )
}

function ChannelDetailIcon({
  logoPath,
  title,
  className,
}: {
  logoPath?: string
  title: string
  className: string
}): JSX.Element {
  return (
    <span className={className} aria-hidden>
      {logoPath ? <img src={logoPath} alt="" /> : title.slice(0, 1).toUpperCase()}
    </span>
  )
}
