import { useEffect, useState, type JSX } from 'react'
import type { TaskCompletionNotificationMode } from '../../../../shared/desktopSettings'
import { useT } from '../../../contexts/LocaleContext'
import { addToast } from '../../../stores/toastStore'
import { PillSwitch } from '../../ui/PillSwitch'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { SettingsSelect } from '../ui/SettingsSelect'

interface NotificationPreferences {
  taskCompletionMode: TaskCompletionNotificationMode
  approvalRequests: boolean
  questions: boolean
}

const TASK_COMPLETION_SELECT_ID = 'settings-task-completion-notification'

export function NotificationsSettingsGroup(): JSX.Element {
  const t = useT()
  const [preferences, setPreferences] = useState<NotificationPreferences>({
    taskCompletionMode: 'whenUnfocused',
    approvalRequests: true,
    questions: true
  })

  useEffect(() => {
    let cancelled = false
    window.api.settings
      .get()
      .then((settings) => {
        if (cancelled) return
        const saved = settings.notifications
        setPreferences({
          taskCompletionMode:
            saved?.taskCompletionMode === 'always' || saved?.taskCompletionMode === 'never'
              ? saved.taskCompletionMode
              : 'whenUnfocused',
          approvalRequests: saved?.approvalRequests !== false,
          questions: saved?.questions !== false
        })
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [])

  async function save(patch: Partial<NotificationPreferences>): Promise<void> {
    const previous = preferences
    setPreferences({ ...previous, ...patch })
    try {
      await window.api.settings.set({ notifications: patch })
    } catch (err) {
      setPreferences(previous)
      addToast(
        t('settings.saveFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  return (
    <SettingsGroup title={t('settings.notifications.title')}>
      <SettingsRow
        label={t('settings.notifications.taskCompletion')}
        description={t('settings.notifications.taskCompletionHint')}
        htmlFor={TASK_COMPLETION_SELECT_ID}
        control={
          <SettingsSelect
            id={TASK_COMPLETION_SELECT_ID}
            value={preferences.taskCompletionMode}
            onValueChange={(mode) => {
              void save({ taskCompletionMode: mode as TaskCompletionNotificationMode })
            }}
            style={{ width: '240px' }}
            options={[
              {
                value: 'whenUnfocused',
                label: t('settings.notifications.taskCompletion.whenUnfocused')
              },
              { value: 'always', label: t('settings.notifications.taskCompletion.always') },
              { value: 'never', label: t('settings.notifications.taskCompletion.never') }
            ]}
          />
        }
      />
      <SettingsRow
        label={t('settings.notifications.approvalRequests')}
        description={t('settings.notifications.approvalRequestsHint')}
        control={
          <PillSwitch
            checked={preferences.approvalRequests}
            aria-label={t('settings.notifications.approvalRequests')}
            onChange={(checked) => {
              void save({ approvalRequests: checked })
            }}
          />
        }
      />
      <SettingsRow
        label={t('settings.notifications.questions')}
        description={t('settings.notifications.questionsHint')}
        control={
          <PillSwitch
            checked={preferences.questions}
            aria-label={t('settings.notifications.questions')}
            onChange={(checked) => {
              void save({ questions: checked })
            }}
          />
        }
      />
    </SettingsGroup>
  )
}
