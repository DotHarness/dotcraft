import type { CSSProperties } from 'react'
import { DecorationSwatch } from '@dotcraft/avatar/react'
import { decorationOf, itemOf, rarityMeta, type ItemId } from '@dotcraft/avatar'
import { normalizeLocale, translate } from '../../shared/locales'
import { showToast } from '../stores/toastStore'
import { usePetStore } from './petStore'

export function showFindToast(id: ItemId): void {
  const locale = normalizeLocale(document.documentElement.lang)
  const rarity = itemOf(id).rarity
  const style = { '--pet-rarity': rarityMeta[rarity].color } as CSSProperties
  showToast({
    key: 'pet-find',
    message: translate(locale, 'pet.find.title', { item: decorationOf(id).name }),
    description: <span className="pet-find-rarity" style={style}>{translate(locale, `settings.pet.rarity.${rarity}`)}</span>,
    art: <span className="pet-find-art" data-rarity={rarity} style={style}><DecorationSwatch id={id} size={30} /></span>,
    action: { label: translate(locale, 'pet.find.wear'), onClick: () => usePetStore.getState().wear(id) }
  })
}
