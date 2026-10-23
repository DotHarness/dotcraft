import { useState, type JSX, type ReactNode } from 'react'
import type { ConfigDescriptorWire } from '../../../shared/channelModules'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { SettingsRow } from '../settings/SettingsGroup'
import { ListOptionsDialog, type OptionAvailability } from './ListOptionsDialog'
import styles from './ModuleConfigForm.module.css'

interface ListOptionsFieldProps {
  descriptor: ConfigDescriptorWire
  label: string
  description: ReactNode
  dialogDescription: string
  channelName: string
  platform: string
  value: string[]
  onChange: (values: string[]) => void
}

export function ListOptionsField({
  descriptor,
  label,
  description,
  dialogDescription,
  channelName,
  platform,
  value,
  onChange
}: ListOptionsFieldProps): JSX.Element {
  const t = useT()
  const [availability, setAvailability] = useState<OptionAvailability | null>(null)

  const open = (): void => {
    setAvailability({ status: 'loading' })
    window.api.appServer
      .sendRequest('externalChannel/configOptionAvailability', { name: channelName, key: descriptor.key })
      .then((result) => {
        const available = (result as { available?: string[] | null }).available
        setAvailability(available ? { status: 'known', available: new Set(available) } : { status: 'unknown' })
      })
      .catch(() => setAvailability({ status: 'unknown' }))
  }

  return (
    <>
      <SettingsRow
        label={label}
        description={description}
        control={(
          <span className={styles.optionsSummary}>
            <span className={styles.optionsCount}>
              {value.length === 0
                ? t('channels.modules.options.none')
                : t('channels.modules.options.count', { count: value.length })}
            </span>
            <Button variant="secondary" size="sm" onClick={open}>
              {t('channels.modules.options.edit')}
            </Button>
          </span>
        )}
      />
      {availability && (
        <ListOptionsDialog
          title={label}
          description={dialogDescription}
          platform={platform}
          options={descriptor.options ?? []}
          optionGroups={descriptor.optionGroups}
          saved={value}
          availability={availability}
          onApply={(values) => {
            onChange(values)
            setAvailability(null)
          }}
          onClose={() => setAvailability(null)}
        />
      )}
    </>
  )
}
