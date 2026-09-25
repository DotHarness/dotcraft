import { useState, type CSSProperties, type JSX } from 'react'
import { Check } from 'lucide-react'
import { DecorationSwatch } from '@dotcraft/avatar/react'
import { itemOf, rarityMeta, slots, type ItemId, type Slot } from '@dotcraft/avatar'
import type { MessageKey } from '../../../../../shared/locales'
import type { PetSettings } from '../../../../../shared/pet'
import { useT } from '../../../../contexts/LocaleContext'
import { bagEntries, isWorn } from '../../../../pet/petModel'
import { Button } from '../../../ui/Button'

interface PetBagPanelProps {
  settings: PetSettings
  onToggle: (id: ItemId) => void
  onTryOn: (id: ItemId | null) => void
}

type BagFilter = Slot | 'all' | 'wearing'
const FILTERS: BagFilter[] = ['all', 'wearing', ...slots]
const COLUMNS = 5
const ROWS = 2
const FILTER_KEYS: Record<BagFilter, MessageKey> = {
  all: 'settings.pet.bag.all', wearing: 'settings.pet.bag.wearing',
  head: 'settings.pet.slot.head', face: 'settings.pet.slot.face', hand: 'settings.pet.slot.hand', back: 'settings.pet.slot.back', skin: 'settings.pet.slot.skin'
}

export function PetBagPanel({ settings, onToggle, onTryOn }: PetBagPanelProps): JSX.Element {
  const t = useT()
  const [filter, setFilter] = useState<BagFilter>('all')
  const { bag, outfit } = settings
  const entries = bagEntries(bag).filter((entry) => filter === 'all' || (filter === 'wearing' ? isWorn(outfit, entry.id) : itemOf(entry.id).slot === filter))
  const slots = Math.max(ROWS * COLUMNS, Math.ceil(entries.length / COLUMNS) * COLUMNS)
  return (
    <div className="pet-settings-bag">
      <div className="pet-settings-filters" role="group" aria-label={t('settings.pet.bag.filter')}>
        {FILTERS.map((option) => (
          <Button key={option} size="sm" variant={filter === option ? 'secondary' : 'ghost'} aria-pressed={filter === option} onClick={() => setFilter(option)}>
            {t(FILTER_KEYS[option])}
          </Button>
        ))}
      </div>
      <div className="pet-settings-bag-scroll">
        <div className="pet-settings-tiles" style={{ '--pet-columns': COLUMNS } as CSSProperties} onMouseLeave={() => onTryOn(null)}>
          {entries.map((entry) => {
            const worn = isWorn(outfit, entry.id)
            const rarity = itemOf(entry.id).rarity
            const name = t(`pet.item.${entry.id}.name` as MessageKey)
            const rarityLabel = t(`settings.pet.rarity.${rarity}` as MessageKey)
            const label = [name, rarityLabel, entry.count > 1 ? t('settings.pet.bag.copies', { count: entry.count }) : '', worn ? t('settings.pet.bag.wearing') : '']
              .filter(Boolean).join(', ')
            return (
              <button key={entry.id} type="button" className="pet-settings-tile" data-worn={worn} data-rarity={rarity} aria-pressed={worn}
                style={{ '--pet-rarity': rarityMeta[rarity].color } as CSSProperties} title={`${name} · ${rarityLabel}`} aria-label={label}
                onMouseEnter={() => onTryOn(entry.id)} onFocus={() => onTryOn(entry.id)} onBlur={() => onTryOn(null)} onClick={() => onToggle(entry.id)}>
                {entry.count > 1 && <span className="pet-settings-count">×{entry.count}</span>}
                <span className="pet-settings-tile-art">
                  <DecorationSwatch id={entry.id} size={44} label={name} />
                  {worn && <span className="pet-settings-tile-worn"><Check size={10} strokeWidth={3} aria-hidden="true" /></span>}
                </span>
                <span className="pet-settings-tile-name">{name}</span>
              </button>
            )
          })}
          {Array.from({ length: slots - entries.length }, (_, index) => <span key={index} className="pet-settings-slot" aria-hidden="true" />)}
        </div>
      </div>
    </div>
  )
}
