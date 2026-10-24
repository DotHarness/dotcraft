import { CircleAlert, TriangleAlert } from 'lucide-react'
import { useEffect, type JSX, type ReactNode } from 'react'
import type { MessageKey } from '../../../../../shared/locales'
import { useT } from '../../../../contexts/LocaleContext'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { Button } from '../../../ui/Button'
import { SettingsPanelSkeleton } from '../../SettingsSkeleton'
import { EnvironmentDraftFields } from './EnvironmentDraftFields'
import type { SaveBlockReason } from './environmentDraft'
import { useEnvironmentDraft } from './useEnvironmentDraft'
import styles from './environments.module.css'

const BLOCK_REASON_KEYS: Record<SaveBlockReason, MessageKey> = {
  saving: 'settings.environments.saveBlocked.saving',
  unreadable: 'settings.environments.saveBlocked.unreadable',
  name: 'settings.environments.saveBlocked.name',
  action: 'settings.environments.saveBlocked.action',
  unchanged: 'settings.environments.saveBlocked.unchanged'
}

export function EnvironmentNotice({ level, title, children, action }: {
  level: 'warning' | 'error'
  title: string
  children?: ReactNode
  action?: ReactNode
}): JSX.Element {
  const Glyph = level === 'error' ? CircleAlert : TriangleAlert
  return (
    <div className={styles.notice} data-level={level} role={level === 'error' ? 'alert' : 'status'}>
      <span className={styles.noticeGlyph}><Glyph size={20} aria-hidden /></span>
      <span className={styles.noticeBody}>
        <span className={styles.noticeTitle}>{title}</span>
        {children ? <span className={styles.noticeText}>{children}</span> : null}
      </span>
      {action}
    </div>
  )
}

export function EnvironmentEditor({
  projectPath,
  path,
  fallbackName,
  onSaved,
  onDirtyChange
}: {
  projectPath: string
  path: string
  fallbackName: string
  onSaved: (path: string) => void
  onDirtyChange: (dirty: boolean) => void
}): JSX.Element {
  const t = useT()
  const controller = useEnvironmentDraft(projectPath, path, fallbackName)
  const { load, draft, update, dirty, blockReason, saveState } = controller
  const saving = saveState === 'saving'

  useEffect(() => {
    onDirtyChange(dirty)
  }, [dirty, onDirtyChange])

  useEffect(() => () => onDirtyChange(false), [onDirtyChange])

  if (load.status === 'loading') return <SettingsPanelSkeleton />

  async function save(): Promise<void> {
    const result = await controller.save()
    if (result) onSaved(result.path)
  }

  const footerButton = saveState === 'conflict' ? (
    <Button variant="primary" onClick={() => void controller.reload()}>
      {t('settings.environments.discard')}
    </Button>
  ) : (
    <ActionTooltip
      label=""
      disabledReason={blockReason && blockReason !== 'saving' ? t(BLOCK_REASON_KEYS[blockReason]) : undefined}
    >
      <Button variant="primary" loading={saving} disabled={blockReason !== null} onClick={() => void save()}>
        {saveState === 'failed' ? t('settings.environments.retry') : t('settings.environments.save')}
      </Button>
    </ActionTooltip>
  )

  return (
    <>
      {load.status === 'unreadable' && (
        <EnvironmentNotice
          level="error"
          title={t('settings.environments.unreadable')}
          action={<Button size="sm" variant="secondary" onClick={() => void controller.reload()}>{t('settings.environments.tryAgain')}</Button>}
        />
      )}
      {load.status === 'ready' && load.error !== undefined && (
        <EnvironmentNotice level="warning" title={t('settings.environments.invalid.title')}>
          {load.error} {t('settings.environments.invalid.replace')}
        </EnvironmentNotice>
      )}

      <EnvironmentDraftFields
        draft={draft}
        update={update}
        disabled={saving || load.status !== 'ready'}
        nameInvalid={load.status === 'ready' && draft.name.trim() === ''}
      />

      {saveState === 'conflict' && (
        <EnvironmentNotice level="warning" title={t('settings.environments.conflict.title')}>
          {t('settings.environments.conflict.description')}
        </EnvironmentNotice>
      )}
      {saveState === 'failed' && (
        <EnvironmentNotice level="error" title={t('settings.environments.failed')} />
      )}

      <div className={styles.footer}>{footerButton}</div>
    </>
  )
}
