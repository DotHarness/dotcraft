import { Pencil } from 'lucide-react'
import { useEffect, useState, type JSX } from 'react'
import type {
  EnvironmentDocument,
  EnvironmentScript,
  ProjectActionDraft,
  ProjectEnvironmentEntry,
  ReadEnvironmentResult
} from '../../../../../shared/projectActions'
import { useT } from '../../../../contexts/LocaleContext'
import { PROJECT_ACTION_GLYPHS } from '../../../projectActions/AddProjectActionDialog'
import { Button } from '../../../ui/Button'
import { DisclosureChevron } from '../../../ui/DisclosureChevron'
import { IconButton } from '../../../ui/IconButton'
import { SettingsGroup } from '../../SettingsGroup'
import { SettingsPanelSkeleton } from '../../SettingsSkeleton'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { scriptOverrides, type ScriptTab } from './environmentDraft'
import { PLATFORM_LABELS, scriptTabOptions } from './environmentPresentation'
import { EnvironmentNotice } from './EnvironmentEditor'
import styles from './environments.module.css'

function ScriptPreviewGroup({ title, script }: { title: string; script: EnvironmentScript }): JSX.Element {
  const t = useT()
  const overrides = scriptOverrides(script)
  const [tab, setTab] = useState<ScriptTab>('default')
  const shown = tab === 'default' ? script.default : script[tab] ?? ''
  return (
    <SettingsGroup title={title}>
      <div className={styles.card}>
        {overrides.length > 0 && (
          <div>
            <SegmentedControl
              ariaLabel={title}
              value={tab}
              options={scriptTabOptions(t, ['default', 'win32', 'darwin', 'linux'])}
              onChange={setTab}
            />
          </div>
        )}
        {shown.trim() ? (
          <pre className={styles.preview}>{shown}</pre>
        ) : (
          <span className={styles.muted}>
            {tab === 'default' ? t('settings.environments.script.none') : t('settings.environments.script.usesDefault')}
          </span>
        )}
      </div>
    </SettingsGroup>
  )
}

function ActionPreview({ action }: { action: ProjectActionDraft }): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const Glyph = PROJECT_ACTION_GLYPHS[action.icon]
  const lines = action.command.split(/\r?\n/)
  const multiline = lines.length > 1
  const toggleLabel = expanded ? t('settings.environments.action.collapse') : t('settings.environments.action.expand')
  return (
    <div className={styles.card}>
      <div className={styles.actionRow}>
        <span className={styles.rowGlyph}><Glyph size={16} aria-hidden /></span>
        <span className={styles.actionName}>{action.name}</span>
        <span className={styles.actionCommand}>{expanded ? '' : lines[0]}</span>
        {action.platform && <span className={styles.muted}>{PLATFORM_LABELS[action.platform]}</span>}
        {multiline && (
          <IconButton
            size={24}
            icon={<DisclosureChevron expanded={expanded} direction="reveal" />}
            label={toggleLabel}
            tooltipLabel={toggleLabel}
            aria-expanded={expanded}
            onClick={() => setExpanded((value) => !value)}
          />
        )}
      </div>
      {expanded && <pre className={`${styles.preview} ${styles.actionDetail}`}>{action.command}</pre>}
    </div>
  )
}

export function EnvironmentView({
  projectPath,
  environment,
  revision,
  onEdit
}: {
  projectPath: string
  environment: ProjectEnvironmentEntry
  revision: number
  onEdit: () => void
}): JSX.Element {
  const t = useT()
  const [result, setResult] = useState<ReadEnvironmentResult | null>(null)

  useEffect(() => {
    let active = true
    window.api.projectActions.readEnvironment({ projectPath, path: environment.path }).then(
      (next) => { if (active) setResult(next) },
      () => { if (active) setResult({ ok: false, reason: 'failed' }) }
    )
    return () => { active = false }
  }, [environment.path, projectPath, revision])

  const document: EnvironmentDocument | null = result?.ok ? result.document : null
  const error = result?.ok ? result.error ?? environment.error : environment.error

  return (
    <>
      <div className={styles.titleRow}>
        <span className={styles.title}>{document?.name || environment.name}</span>
        <Button size="sm" variant="secondary" iconLeft={<Pencil size={15} aria-hidden />} onClick={onEdit}>
          {t('settings.environments.edit')}
        </Button>
      </div>
      {result === null ? <SettingsPanelSkeleton /> : (
        <>
          {!result.ok && <EnvironmentNotice level="error" title={t('settings.environments.unreadable')} />}
          {result.ok && error && (
            <EnvironmentNotice level="warning" title={t('settings.environments.invalid.title')}>{error}</EnvironmentNotice>
          )}
          {document && (
            <>
              <ScriptPreviewGroup title={t('settings.environments.setup.title')} script={document.setup} />
              <ScriptPreviewGroup title={t('settings.environments.cleanup.title')} script={document.cleanup} />
              <SettingsGroup title={t('settings.environments.actions.title')}>
                {document.actions.length === 0 ? (
                  <div className={styles.empty}>{t('settings.environments.actions.empty')}</div>
                ) : document.actions.map((action, index) => (
                  <ActionPreview key={`${index}:${action.name}`} action={action} />
                ))}
              </SettingsGroup>
            </>
          )}
        </>
      )}
    </>
  )
}
