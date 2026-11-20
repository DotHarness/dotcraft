import { Button, InlineDiff, Input, SplitButton, Textarea } from '@dotcraft/plugin'
import { useEffect, useMemo, useState, type JSX } from 'react'
import { useT } from '../i18n'
import { changedSpan } from '../shared/ui'
import {
  POLICY_LIMITS,
  settingEdit,
  settingEquals,
  validatePolicy,
  type PolicyLayerKind,
  type PolicySetting,
  type PolicyState,
  type PolicyValues
} from './policy'
import { editorOrigin, editorSettings, type PolicyEditorSetting } from './PolicyTab'
import { TypesEditor } from './TypesEditor'
import { usePolicyWrite } from './usePolicyWrite'
import styles from './policy.module.css'

function displayValue(setting: PolicyEditorSetting, values: PolicyValues): string {
  if (setting === 'types') {
    return values.types.map((type) => `${type.name}${type.personal ? ' (personal)' : ''}: ${type.description}`).join('\n')
  }
  return values[setting].replace(/\r\n/g, '\n').trim()
}

export function PolicySettingEditor({ state, setting, onClose, onChanged }: {
  state: PolicyState
  setting: PolicyEditorSetting
  onClose(): void
  onChanged(): void
}): JSX.Element {
  const t = useT()
  const { busy, write } = usePolicyWrite(onChanged)
  const [draft, setDraft] = useState<PolicyValues>(state.effective)
  // Edits and layer versions stay relative to what the page opened with, so a change saved elsewhere meanwhile
  // fails as a version conflict instead of being overwritten. After a conflict the reloaded state becomes the base.
  const [base, setBase] = useState<PolicyState>(state)
  const [rebase, setRebase] = useState(false)
  const [comparing, setComparing] = useState(false)
  useEffect(() => {
    if (!rebase || state === base) return
    setBase(state)
    setRebase(false)
  }, [rebase, state, base])

  const settings = editorSettings(setting)
  const issues = useMemo(() => validatePolicy(draft), [draft])
  const issue = settings.map((candidate) => issues[candidate]).find((value) => value != null)
  const changed = settings.filter((candidate) => !settingEquals(candidate, draft[candidate], base.effective[candidate]))
  const customized = editorOrigin(state, setting) !== 'default'
  const differsFromDefault = settings.some((candidate) => !settingEquals(candidate, draft[candidate], state.defaults[candidate]))
  const span = comparing ? changedSpan(displayValue(setting, state.defaults), displayValue(setting, draft)) : null

  const update = <S extends PolicySetting>(key: S, value: PolicyValues[S]): void => setDraft((previous) => ({ ...previous, [key]: value }))

  const save = async (layer: PolicyLayerKind): Promise<void> => {
    const outcome = await write(base.layers[layer], changed.map((candidate) => settingEdit(candidate, draft[candidate])))
    if (outcome === 'saved') onClose()
    else if (outcome === 'conflict') setRebase(true)
  }
  const canSave = !busy && changed.length > 0 && issue == null

  const restore = async (): Promise<void> => {
    for (const layer of ['workspace', 'user'] as const) {
      const edits = settings.filter((candidate) => state.origins[candidate] === layer).map((candidate) => settingEdit(candidate, null))
      if (edits.length > 0 && await write(state.layers[layer], edits) !== 'saved') return
    }
    onClose()
  }

  return (
    <div className={styles.stack}>
      <div className={styles.editor}>
        {setting === 'extractionGuidance' ? (
          <label className={styles.interval}>
            <span>{t('policy.setting.extractionInterval')}</span>
            <Input
              type="number"
              aria-label={t('policy.setting.extractionInterval')}
              min={POLICY_LIMITS.minInterval}
              max={POLICY_LIMITS.maxInterval}
              disabled={busy}
              value={String(draft.extractionInterval)}
              onChange={(event) => update('extractionInterval', Number(event.target.value))}
              style={{ width: 72 }}
            />
            <span>{t('policy.setting.extractionInterval.unit')}</span>
          </label>
        ) : null}
        {setting === 'types' ? (
          <TypesEditor types={draft.types} disabled={busy} onChange={(types) => update('types', types)} />
        ) : (
          <Textarea
            aria-label={t(`policy.setting.${setting}` as const)}
            rows={14}
            disabled={busy}
            value={draft[setting]}
            onChange={(event) => update(setting, event.target.value)}
          />
        )}
        {issue ? <div className={styles.issue}>{t(`policy.issue.${issue}` as const)}</div> : null}
      </div>

      {span && differsFromDefault ? (
        <InlineDiff filePath={t('policy.defaultVsDraft')} line={span.line} before={span.before} after={span.after} />
      ) : null}

      <div className={styles.footer}>
        {differsFromDefault ? (
          <Button variant="ghost" onClick={() => setComparing((value) => !value)}>
            {comparing ? t('policy.hideComparison') : t('policy.compare')}
          </Button>
        ) : null}
        {customized ? <Button variant="ghost" disabled={busy} onClick={() => void restore()}>{t('policy.restore')}</Button> : null}
        <span className={styles.spacer} />
        <Button variant="ghost" disabled={busy} onClick={onClose}>{t('policy.cancel')}</Button>
        {state.layers.workspace ? (
          <SplitButton
            label={t('policy.save')}
            menuLabel={t('policy.saveOptions')}
            disabled={!canSave}
            onClick={() => void save('user')}
            items={[{ label: t('policy.saveToWorkspace'), onSelect: () => void save('workspace') }]}
          />
        ) : (
          <Button variant="primary" loading={busy} disabled={!canSave} onClick={() => void save('user')}>{t('policy.save')}</Button>
        )}
      </div>
    </div>
  )
}
