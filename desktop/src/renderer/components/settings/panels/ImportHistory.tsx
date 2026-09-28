import { useEffect, useState, type JSX } from 'react'
import type { ImportCompletedNotification, ImportOutcome } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { useUIStore } from '../../../stores/uiStore'
import { useThreadStore } from '../../../stores/threadStore'
import { usePluginStore } from '../../../stores/pluginStore'
import { useSkillsStore } from '../../../stores/skillsStore'
import { Button } from '../../ui/Button'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import styles from './ImportPanel.module.css'

function openOutcome(outcome: ImportOutcome): void {
  const ui = useUIStore.getState()
  if (outcome.threadId) {
    useThreadStore.getState().setActiveThreadId(outcome.threadId)
    ui.setActiveMainView('conversation')
  } else if (outcome.category === 'skills' || outcome.category === 'plugins') {
    ui.setPluginCatalogSurface(outcome.category === 'plugins' ? 'plugins' : 'skills')
    ui.setActiveMainView('skills')
    if (outcome.category === 'plugins') {
      const pluginId = outcome.targetPath.split(/[\\/]/).at(-1) ?? ''
      void usePluginStore.getState().selectPlugin(pluginId)
    } else if (outcome.title) {
      void useSkillsStore.getState().selectSkill(outcome.title)
    }
  } else if (outcome.category === 'commands') {
    ui.setActiveMainView('conversation')
    ui.setComposerPrefill(`/${outcome.title ?? ''} `)
  } else {
    ui.setActiveSettingsTab(outcome.category === 'mcp' ? 'mcp' : outcome.category === 'hooks' ? 'hooks' : 'personalization')
  }
}

export function ImportHistory({ revision }: { revision: number }): JSX.Element {
  const t = useT()
  const [batches, setBatches] = useState<ImportCompletedNotification[]>([])
  const [attention, setAttention] = useState<ImportOutcome[]>([])
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let disposed = false
    void window.api.appServer.sendRequest('import/history/list', {}).then(result => {
      if (disposed) return
      setBatches(result.imports)
      setAttention(result.attention)
      setError(null)
    }).catch(reason => {
      if (!disposed) setError(String(reason))
    })
    return () => { disposed = true }
  }, [revision])
  return <>
    {attention.length > 0 && <SettingsGroup title={t('settings.import.setup.attention')}>
      {attention.map((outcome, index) => <SettingsRow key={`${outcome.sourceId}-${index}`} label={outcome.title}
        description={t(`settings.import.category.${outcome.category}`)}
        control={<Button onClick={() => openOutcome(outcome)}>{t('settings.import.setup.manage')}</Button>} />)}
    </SettingsGroup>}
    <SettingsGroup title={t('settings.import.setup.history')}>
      {error && <p role="alert" className={styles.error}>{error}</p>}
      {batches.length === 0 && !error && <SettingsRow description={t('settings.import.setup.noHistory')} />}
      {batches.map(batch => <SettingsRow key={batch.importId}><details className={styles.sourceText}>
        <summary>{new Date(batch.completedAt).toLocaleString()} · {t('settings.import.setup.summary', {
          imported: batch.outcomes.filter(o => o.status === 'imported' || o.status === 'appended' || o.status === 'attention').length,
          failed: batch.outcomes.filter(o => o.status === 'failed' || o.status === 'unsupported').length
        })}</summary>
        {batch.outcomes.map((outcome, index) => <div key={`${outcome.sourceId}-${index}`} className={styles.historyItem}>
          <span>{outcome.title ?? outcome.sourceId} · {t(`settings.import.status.${outcome.status}`)}
            {outcome.errorCode && <small className={styles.historyReason}>{outcome.errorCode}</small>}</span>
          {(outcome.threadId || ['imported', 'attention', 'existing'].includes(outcome.status) && ['skills', 'instructions', 'commands', 'plugins', 'mcp', 'hooks'].includes(outcome.category)) &&
            <Button onClick={() => openOutcome(outcome)}>{t('settings.import.setup.manage')}</Button>}
        </div>)}
      </details></SettingsRow>)}
    </SettingsGroup>
  </>
}
