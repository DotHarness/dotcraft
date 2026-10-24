import { useState, type JSX, type ReactNode } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { Textarea } from '../../../ui/Input'
import { SettingsGroup } from '../../SettingsGroup'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { SCRIPT_TABS, type ScriptDraft, type ScriptTab } from './environmentDraft'
import { scriptTabOptions } from './environmentPresentation'
import styles from './environments.module.css'

export function ScriptEditorGroup({
  title,
  description,
  headerAction,
  placeholder,
  value,
  disabled,
  onChange
}: {
  title: string
  description: string
  headerAction?: ReactNode
  placeholder: string
  value: ScriptDraft
  disabled: boolean
  onChange: (value: ScriptDraft) => void
}): JSX.Element {
  const t = useT()
  const [tab, setTab] = useState<ScriptTab>('default')
  return (
    <SettingsGroup title={title} description={description} headerAction={headerAction}>
      <div className={styles.card}>
        <div>
          <SegmentedControl
            ariaLabel={title}
            value={tab}
            options={scriptTabOptions(t, SCRIPT_TABS)}
            onChange={setTab}
          />
        </div>
        <Textarea
          mono
          rows={5}
          className={styles.code}
          aria-label={title}
          value={value[tab]}
          disabled={disabled}
          placeholder={tab === 'default' ? placeholder : t('settings.environments.script.overridePlaceholder')}
          onChange={(event) => onChange({ ...value, [tab]: event.target.value })}
        />
      </div>
    </SettingsGroup>
  )
}
