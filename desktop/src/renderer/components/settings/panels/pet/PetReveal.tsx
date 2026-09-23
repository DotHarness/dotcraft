import type { CSSProperties, JSX } from 'react'
import { DecorationSwatch } from '@dotcraft/avatar/react'
import { decorationName, decorationOf, itemOf, rarityMeta, type ItemId } from '@dotcraft/avatar'
import type { MessageKey } from '../../../../../shared/locales'
import type { PetSettings } from '../../../../../shared/pet'
import { useT } from '../../../../contexts/LocaleContext'
import { wouldClear } from '../../../../pet/petModel'
import { Button } from '../../../ui/Button'

interface PetRevealProps {
  id: ItemId
  duplicate: boolean
  settings: PetSettings
  onWear: (id: ItemId) => void
  onDismiss: () => void
}

export function PetReveal({ id, duplicate, settings, onWear, onDismiss }: PetRevealProps): JSX.Element {
  const t = useT()
  const item = decorationOf(id)
  const rarity = itemOf(id).rarity
  const worn = settings.outfit[item.slot]
  const clears = wouldClear(settings, id)
  const replaced = worn !== 'none' && worn !== id
    ? decorationName(worn)
    : clears.length ? clears.map((slot) => decorationName(settings.outfit[slot])).join(', ') : null
  return (
    <div className="pet-settings-reveal" role="status" data-rarity={rarity} style={{ '--pet-rarity': rarityMeta[rarity].color } as CSSProperties}>
      <span className="pet-settings-reveal-art"><DecorationSwatch id={id} size={96} /></span>
      <div className="pet-settings-reveal-copy">
        <span className="pet-settings-eyebrow">{t('settings.pet.reveal.eyebrow')}{duplicate ? ` · ${t('settings.pet.reveal.duplicate')}` : ''}</span>
        <h3>{item.name}</h3>
        <p>{t(`settings.pet.slot.${item.slot}` as MessageKey)} · {item.feature}</p>
        <div className="pet-settings-reveal-actions">
          <span className="pet-settings-rarity-word">{t(`settings.pet.rarity.${rarity}` as MessageKey)}</span>
          <Button size="sm" variant="primary" disabled={worn === id} onClick={() => onWear(id)}>
            {worn === id ? t('settings.pet.reveal.wearing') : replaced ? t('settings.pet.reveal.replace', { name: replaced }) : t('settings.pet.reveal.wear')}
          </Button>
          <Button size="sm" variant="ghost" onClick={onDismiss}>{t('settings.pet.reveal.keep')}</Button>
        </div>
      </div>
    </div>
  )
}
