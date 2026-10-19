import { useState, type FocusEvent, type JSX, type KeyboardEvent } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useConfigSetting } from '../../../../stores/configStore'
import { SecretInput } from '../../../channels/FormShared'
import { SettingsRow } from '../../SettingsGroup'
import {
  SECRET_KEY_MASK,
  isSecretStored,
  resolveSecretKeyCommit
} from './webSearchModel'
import styles from './WebSearchSettingsGroup.module.css'

interface Props {
  provider: 'Exa' | 'DeepSeek'
  optional?: boolean
}

export function WebSearchApiKeyRow({ provider, optional = false }: Props): JSX.Element {
  const t = useT()
  const setting = useConfigSetting(`Tools.Web.${provider}ApiKey`, (error) =>
    t('settings.llm.webSearch.saveFailed', { error })
  )
  const stored = isSecretStored(setting.value)
  const [draft, setDraft] = useState<string | null>(null)
  const id = `settings-web-search-${provider.toLowerCase()}-key`
  const commit = (): void => {
    const change = resolveSecretKeyCommit(draft, stored)
    setDraft(null)
    if (change.kind === 'set') void setting.set(change.value)
    else if (change.kind === 'clear') void setting.set(null)
  }
  const blur = (event: FocusEvent<HTMLDivElement>): void => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null))
      commit()
  }
  const keyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    if (event.key === 'Enter') commit()
    else if (event.key === 'Escape') setDraft(null)
  }
  return (
    <SettingsRow
      label={
        provider === 'Exa'
          ? t('settings.llm.webSearch.exaKey')
          : t('settings.llm.webSearch.deepSeekKey')
      }
      description={
        provider === 'DeepSeek'
          ? t('settings.llm.webSearch.deepSeekKeyHint')
          : optional
            ? t('settings.llm.webSearch.exaKeyAutoHint')
            : t('settings.llm.webSearch.exaKeyHint')
      }
      htmlFor={id}
      control={
        <div className={styles.control} onBlur={blur} onKeyDown={keyDown}>
          <SecretInput
            id={id}
            value={draft ?? (stored ? SECRET_KEY_MASK : '')}
            placeholder={t('settings.llm.webSearch.exaKeyPlaceholder')}
            disabled={setting.pending}
            onChange={setDraft}
            mono
          />
        </div>
      }
    />
  )
}
