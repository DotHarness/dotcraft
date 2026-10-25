import type { JSX } from 'react'
import type { NewTaskPromptMode } from '../../../../shared/threadTasks'
import { useT } from '../../../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../../../stores/composerPreferencesStore'
import { addToast } from '../../../stores/toastStore'
import { SettingsRow } from '../SettingsGroup'
import { SegmentedControl } from '../ui/SegmentedControl'

export function NewTaskBehaviorRow(): JSX.Element {
  const t = useT()
  const mode = useComposerPreferencesStore((state) => state.newTaskPrompt)
  const save = useComposerPreferencesStore((state) => state.saveNewTaskPrompt)

  async function changeMode(next: NewTaskPromptMode): Promise<void> {
    try {
      await save(next)
    } catch (error) {
      addToast(t('settings.newTaskBehavior.saveFailed', {
        error: error instanceof Error ? error.message : String(error)
      }), 'error')
    }
  }

  return (
    <SettingsRow
      label={t('settings.newTaskBehavior.label')}
      description={t('settings.newTaskBehavior.description')}
      control={
        <SegmentedControl
          value={mode}
          options={[
            { value: 'ask', label: t('settings.newTaskBehavior.ask') },
            { value: 'newThread', label: t('threadActionCard.untitled') },
            { value: 'here', label: t('settings.newTaskBehavior.here') }
          ]}
          ariaLabel={t('settings.newTaskBehavior.label')}
          onChange={(next) => { void changeMode(next) }}
        />
      }
    />
  )
}
