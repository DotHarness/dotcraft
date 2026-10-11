import { useEffect, useState, type JSX } from 'react'
import type { ChatsFolderInfo } from '../../../../shared/chatFolders'
import { useT } from '../../../contexts/LocaleContext'
import { addToast } from '../../../stores/toastStore'
import { Button } from '../../ui/Button'
import { SettingsRow } from '../SettingsGroup'
import styles from './ChatsFolderRow.module.css'

export function ChatsFolderRow(): JSX.Element {
  const t = useT()
  const [folder, setFolder] = useState<ChatsFolderInfo | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    let cancelled = false
    void window.api.workspace.getChatsFolder().then((info) => {
      if (!cancelled) setFolder(info)
    })
    return () => { cancelled = true }
  }, [])

  async function save(chatsFolder: string): Promise<void> {
    setSaving(true)
    try {
      await window.api.settings.set({ chatsFolder })
      setFolder(await window.api.workspace.getChatsFolder())
    } catch (error) {
      addToast(t('settings.chatsFolder.saveFailed', {
        error: error instanceof Error ? error.message : String(error)
      }), 'error')
    } finally {
      setSaving(false)
    }
  }

  async function change(): Promise<void> {
    const picked = await window.api.workspace.pickFolder({ title: t('settings.chatsFolder.pickTitle') })
    if (picked) await save(picked)
  }

  return (
    <SettingsRow
      label={t('settings.chatsFolder.label')}
      description={t('settings.chatsFolder.description')}
      control={(
        <div className={styles.actions}>
          {folder && <span className={styles.path} title={folder.path}>{folder.path}</span>}
          <Button variant="secondary" disabled={saving} onClick={() => { void change() }}>
            {t('settings.chatsFolder.change')}
          </Button>
          {folder?.customized && (
            <Button variant="ghost" disabled={saving} onClick={() => { void save('') }}>
              {t('settings.chatsFolder.reset')}
            </Button>
          )}
        </div>
      )}
    />
  )
}
