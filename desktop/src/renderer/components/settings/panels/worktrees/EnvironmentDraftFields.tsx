import { Plus } from 'lucide-react'
import { useId, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { SettingsGroup } from '../../SettingsGroup'
import { newActionDraft, type EnvironmentDraft } from './environmentDraft'
import { ActionEditorCard } from './ActionEditorCard'
import { ScriptEditorGroup } from './ScriptEditorGroup'
import { VariablesButton } from './VariablesButton'
import styles from './environments.module.css'

export function EnvironmentDraftFields({
  draft,
  update,
  disabled,
  nameInvalid
}: {
  draft: EnvironmentDraft
  update: (change: (draft: EnvironmentDraft) => EnvironmentDraft) => void
  disabled: boolean
  nameInvalid: boolean
}): JSX.Element {
  const t = useT()
  const nameId = useId()

  return (
    <>
      <SettingsGroup>
        <div className={styles.card}>
          <div className={styles.field}>
            <label className={styles.label} htmlFor={nameId}>{t('settings.environments.name')}</label>
            <Input
              id={nameId}
              value={draft.name}
              invalid={nameInvalid}
              disabled={disabled}
              onChange={(event) => update((current) => ({ ...current, name: event.target.value }))}
            />
          </div>
        </div>
      </SettingsGroup>

      <ScriptEditorGroup
        title={t('settings.environments.setup.title')}
        description={t('settings.environments.setup.description')}
        headerAction={<VariablesButton />}
        placeholder="npm install"
        value={draft.setup}
        disabled={disabled}
        onChange={(setup) => update((current) => ({ ...current, setup }))}
      />

      <ScriptEditorGroup
        title={t('settings.environments.cleanup.title')}
        description={t('settings.environments.cleanup.description')}
        placeholder="docker compose down"
        value={draft.cleanup}
        disabled={disabled}
        onChange={(cleanup) => update((current) => ({ ...current, cleanup }))}
      />

      <SettingsGroup
        title={t('settings.environments.actions.title')}
        description={t('settings.environments.actions.description')}
        headerAction={(
          <Button
            size="sm"
            variant="secondary"
            iconLeft={<Plus size={15} aria-hidden />}
            disabled={disabled}
            onClick={() => update((current) => ({ ...current, actions: [...current.actions, newActionDraft()] }))}
          >
            {t('settings.environments.actions.add')}
          </Button>
        )}
        framed={false}
      >
        {draft.actions.length === 0 ? (
          <div className={styles.actionsEmpty}>{t('settings.environments.actions.empty')}</div>
        ) : <div className={styles.actionList}>{draft.actions.map((action, index) => (
          <ActionEditorCard
            key={action.key}
            action={action}
            disabled={disabled}
            onChange={(next) => update((current) => ({
              ...current,
              actions: current.actions.map((candidate, candidateIndex) => (candidateIndex === index ? next : candidate))
            }))}
            onDelete={() => update((current) => ({
              ...current,
              actions: current.actions.filter((_, candidateIndex) => candidateIndex !== index)
            }))}
          />
        ))}</div>}
      </SettingsGroup>
    </>
  )
}
