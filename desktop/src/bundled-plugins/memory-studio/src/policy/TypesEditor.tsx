import { Button, IconButton, Input, PillSwitch } from '@dotcraft/plugin'
import { Plus, X } from 'lucide-react'
import type { JSX } from 'react'
import { useT } from '../i18n'
import { POLICY_LIMITS, type MemoryType } from './policy'
import styles from './policy.module.css'

export function TypesEditor({ types, disabled, onChange }: {
  types: readonly MemoryType[]
  disabled?: boolean
  onChange(types: MemoryType[]): void
}): JSX.Element {
  const t = useT()
  const update = (index: number, patch: Partial<MemoryType>): void =>
    onChange(types.map((type, at) => at === index ? { ...type, ...patch } : type))
  return (
    <div className={styles.types}>
      {types.map((type, index) => (
        <div key={index} className={styles.typeRow}>
          <Input
            aria-label={t('policy.typeName')}
            placeholder={t('policy.typeName')}
            mono
            disabled={disabled}
            maxLength={POLICY_LIMITS.maxTypeName}
            value={type.name}
            onChange={(event) => update(index, { name: event.target.value })}
          />
          <Input
            aria-label={t('policy.typeDescription')}
            placeholder={t('policy.typeDescription')}
            disabled={disabled}
            maxLength={POLICY_LIMITS.maxTypeDescription}
            value={type.description}
            onChange={(event) => update(index, { description: event.target.value })}
          />
          <label className={styles.typePersonal}>
            <PillSwitch
              checked={type.personal}
              disabled={disabled}
              aria-label={t('policy.typePersonalFor', { name: type.name || '…' })}
              onChange={(personal) => update(index, { personal })}
            />
            <span>{t('policy.typePersonal')}</span>
          </label>
          <IconButton
            icon={<X size={14} />}
            label={t('policy.removeType', { name: type.name || '…' })}
            disabled={disabled || types.length <= POLICY_LIMITS.minTypes}
            onClick={() => onChange(types.filter((_, at) => at !== index))}
          />
        </div>
      ))}
      <div>
        <Button
          variant="ghost"
          size="sm"
          iconLeft={<Plus size={14} />}
          disabled={disabled || types.length >= POLICY_LIMITS.maxTypes}
          onClick={() => onChange([...types, { name: '', description: '', personal: false }])}
        >
          {t('policy.addType')}
        </Button>
      </div>
    </div>
  )
}
