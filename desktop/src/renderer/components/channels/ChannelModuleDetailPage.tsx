import { type ReactNode } from 'react'
import { Settings } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { CatalogBreadcrumb, CatalogTopBar } from '../catalog/CatalogSurface'
import { IconButton } from '../ui/IconButton'
import { MorphingActionPill } from '../plugins/MorphingActionPill'
import { IdentityMark, type IdentityMarkRole } from '../ui/IdentityMark'
import { IdentityMarkFallback } from '../ui/IdentityMarkFallback'
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
  setupSteps?: string[]
  infoItems: ChannelDetailInfoItem[]
  active: boolean
  busy: boolean
  controlsAvailable: boolean
  mode: ChannelModuleDetailMode
  onModeChange: (mode: ChannelModuleDetailMode) => void
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
  setupSteps = [],
  infoItems,
  active,
  busy,
  controlsAvailable,
  mode,
  onModeChange,
  onBack,
  onToggleConnection,
  renderManage,
}: ChannelModuleDetailPageProps): JSX.Element {
  const t = useT()

  if (mode === 'manage') {
    const showPreview = (): void => onModeChange('preview')
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
              <ChannelDetailIcon logoPath={logoPath} role="hero" />
            </div>
            <div className={styles.identityRow}>
              <div className={styles.identity}>
                <h1>{title}</h1>
                <p>{subtitle}</p>
              </div>
              <div className={styles.actions}>
                <IconButton
                  icon={<Settings size={16} aria-hidden />}
                  label={t('plugins.manage')}
                  tooltipLabel={t('plugins.manage')}
                  onClick={() => onModeChange('manage')}
                />
                <MorphingActionPill
                  label={t(active ? 'appBinding.disconnect' : 'appBinding.connect')}
                  loading={busy}
                  disabled={!controlsAvailable}
                  onClick={onToggleConnection}
                />
              </div>
            </div>
          </header>

          <div className={styles.preview} aria-label={t('channels.detail.preview')}>
            <div className={styles.prompt}>
              <span className={styles.promptPrefix}>
                <ChannelDetailIcon logoPath={logoPath} role="compact" size={18} />
                <strong>{title}</strong>
              </span>
              <span className={styles.promptText}>{previewPrompt}</span>
            </div>
          </div>

          <p className={styles.description}>{description}</p>

          {setupSteps.length > 0 && (
            <section className={styles.setup}>
              <h2>{t('channels.detail.setup')}</h2>
              <ol>
                {setupSteps.map((step, index) => (
                  <li key={index}>
                    <span className={styles.stepNumber} aria-hidden>{index + 1}</span>
                    <span>{renderStepText(step)}</span>
                  </li>
                ))}
              </ol>
            </section>
          )}

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
  role,
  size,
}: {
  logoPath?: string
  role: IdentityMarkRole
  size?: number
}): JSX.Element {
  return <IdentityMark role={role} size={size} src={logoPath} fallback={<IdentityMarkFallback kind="channel" />} />
}

const STEP_TOKEN = /`([^`]+)`|\[([^\]]+)\]\((https:\/\/[^)\s]+)\)/g

function renderStepText(step: string): ReactNode[] {
  const parts: ReactNode[] = []
  let cursor = 0
  for (const match of step.matchAll(STEP_TOKEN)) {
    if (match.index > cursor) parts.push(step.slice(cursor, match.index))
    const [, code, label, href] = match
    parts.push(
      code !== undefined ? (
        <code key={match.index}>{code}</code>
      ) : (
        <a
          key={match.index}
          href={href}
          onClick={(event) => {
            event.preventDefault()
            void window.api.shell.openExternal(href)
          }}
        >
          {label}
        </a>
      )
    )
    cursor = match.index + match[0].length
  }
  if (cursor < step.length) parts.push(step.slice(cursor))
  return parts
}
