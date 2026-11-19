import { Button, Menu, SettingsGroup, SettingsRow } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { useT, type MessageKey } from '../i18n'
import { memoryStudioHost } from '../runtime'
import type { PolicyOrigin, PolicySetting, PolicyState } from './policy'
import { matchingPreset, PRESETS, presetEdits, replacesLayerValues, type Preset } from './presets'
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
  const matched = matchingPreset(state.effective, state.defaults)

  // Presets write the personal layer, the one a workspace layer may still override.
  const applyPreset = async (preset: Preset): Promise<void> => {
    const layer = state.layers.user
    if (replacesLayerValues(preset, layer, state.defaults)) {
      const confirmed = await memoryStudioHost().ui.confirm({
        title: t('presets.confirmTitle', { name: t(`preset.${preset.id}` as const) }),
        message: t('presets.confirmUser'),
        confirmLabel: t('presets.apply')
      })
      if (!confirmed) return
    }
    await write(layer, presetEdits(preset))
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
    <div className={styles.stack}>
      <SettingsGroup>
        <SettingsRow
          label={t('presets.label')}
          description={t('presets.current', { name: matched ? t(`preset.${matched}` as const) : t('presets.customized') })}
          control={(
            <Menu items={PRESETS.map((preset) => ({
              label: t(`preset.${preset.id}` as const),
              checked: matched === preset.id,
              disabled: busy,
              onSelect: () => void applyPreset(preset)
            }))}>
              <Button variant="secondary" size="sm" disabled={busy}>{t('presets.apply')}</Button>
            </Menu>
          )}
        />
      </SettingsGroup>

      <SettingsGroup title={t('policy.group.remember')}>
        {row('types')}
      </SettingsGroup>

      <SettingsGroup title={t('policy.group.write')}>
        {row('saveGuidance')}
        {row('extractionGuidance')}
        {row('consolidationGuidance')}
      </SettingsGroup>
    </div>
  )
}
