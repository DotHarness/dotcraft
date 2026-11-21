import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { SecretInput } from './FormShared'
import styles from './ModuleConfigForm.module.css'

export function ChannelSecretInput({
  label,
  kind,
  configured,
  onChange
}: {
  label: string
  kind: string
  configured: boolean
  onChange: (value: unknown, valid: boolean) => void
}): JSX.Element {
  const t = useT()
  const [raw, setRaw] = useState('')
  const [invalid, setInvalid] = useState(false)
  const array = kind === 'list'
  const object = kind === 'object'

  function edit(value: string): void {
    setRaw(value)
    if (!value || (!array && !object)) {
      setInvalid(false)
      onChange(value || undefined, true)
      return
    }
    try {
      const parsed: unknown = JSON.parse(value)
      const valid = array ? Array.isArray(parsed) : parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
      setInvalid(!valid)
      onChange(parsed, valid)
    } catch {
      setInvalid(true)
      onChange(undefined, false)
    }
  }

  return (
    <div className={styles.fullWidthControl}>
      <SecretInput ariaLabel={label} value={raw} onChange={edit}
        placeholder={configured ? t('channels.plugins.secretSaved') : array ? '[]' : object ? '{}' : undefined}
      />
      {configured && <Button size="sm" variant="secondary" onClick={() => edit('')}>
        {t('channels.plugins.clearSecret')}
      </Button>}
      {invalid && <span role="alert">{t(array ? 'channels.plugins.secretArray' : 'channels.plugins.secretObject')}</span>}
    </div>
  )
}
