import { Button, IconButton, SettingsGroup, SettingsRow } from '@dotcraft/plugin'
import { RotateCcw } from 'lucide-react'
import type { JSX } from 'react'
import { useT, type MessageKey } from '../i18n'
import { memoryStudioHost } from '../runtime'
import { POLICY_SETTINGS, settingEdit, type PolicyOrigin, type PolicySetting, type PolicyState } from './policy'
import { usePolicyWrite } from './usePolicyWrite'
import styles from './policy.module.css'

/** The extraction interval has no page of its own: it is edited on the extraction guidance page. */
export type PolicyEditorSetting = Exclude<PolicySetting, 'extractionInterval'>

const MODIFIED_KEYS: Partial<Record<PolicyOrigin, MessageKey>> = {
  user: 'policy.modified.user',
  workspace: 'policy.modified.workspace'
}

export function editorSettings(setting: PolicyEditorSetting): PolicySetting[] {
  return setting === 'extractionGuidance' ? ['extractionGuidance', 'extractionInterval'] : [setting]
}

/** The nearest layer that sets any of the page's settings. */
export function editorOrigin(state: PolicyState, setting: PolicyEditorSetting): PolicyOrigin {
  const origins = editorSettings(setting).map((candidate) => state.origins[candidate])
  return origins.includes('workspace') ? 'workspace' : origins.includes('user') ? 'user' : 'default'
}

export function PolicyTab({ state, onEdit, onChanged }: {
  state: PolicyState
  onEdit(setting: PolicyEditorSetting): void
  onChanged(): void
}): JSX.Element {
  const t = useT()
  const { busy, write } = usePolicyWrite(onChanged)
  const hasCustomizations = POLICY_SETTINGS.some((setting) => state.origins[setting] !== 'default')

  const restoreAll = async (): Promise<void> => {
    const confirmed = await memoryStudioHost().ui.confirm({
      title: t('policy.restoreAllTitle'),
      message: t('policy.restoreAllMessage'),
      confirmLabel: t('policy.restore')
    })
    if (!confirmed) return
    for (const layer of ['workspace', 'user'] as const) {
      const edits = POLICY_SETTINGS
        .filter((setting) => state.layers[layer]?.values[setting] !== undefined)
        .map((setting) => settingEdit(setting, null))
      if (edits.length > 0 && await write(state.layers[layer], edits) !== 'saved') return
    }
  }

  const row = (setting: PolicyEditorSetting): JSX.Element => {
    const modified = MODIFIED_KEYS[editorOrigin(state, setting)]
    return (
      <SettingsRow
        key={setting}
        label={(
          <>
            {t(`policy.setting.${setting}` as const)}
            {modified ? <span className={styles.qualifier}>{t(modified)}</span> : null}
          </>
        )}
        description={t(`policy.setting.${setting}.description` as const)}
        control={<Button variant="secondary" size="sm" onClick={() => onEdit(setting)}>{t('policy.edit')}</Button>}
      />
    )
  }

  return (
    <SettingsGroup
      title={t('policy.group.title')}
      headerAction={hasCustomizations ? (
        <IconButton
          icon={<RotateCcw size={14} />}
          label={t('policy.restoreAll')}
          tooltipLabel={t('policy.restoreAll')}
          disabled={busy}
          onClick={() => void restoreAll()}
        />
      ) : undefined}
    >
      {row('types')}
      {row('saveGuidance')}
      {row('extractionGuidance')}
      {row('consolidationGuidance')}
    </SettingsGroup>
  )
}
