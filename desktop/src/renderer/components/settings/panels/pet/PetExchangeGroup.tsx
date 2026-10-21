import { useState, type CSSProperties, type JSX } from 'react'
import { DecorationSwatch } from '@dotcraft/avatar/react'
import { itemOf, rarityMeta, type ItemId, type Rarity } from '@dotcraft/avatar'
import type { MessageKey } from '../../../../../shared/locales'
import type { PetSettings } from '../../../../../shared/pet'
import { useT } from '../../../../contexts/LocaleContext'
import { showExchangeToast } from '../../../../pet/findToast'
import { EXCHANGE_COUNT, autoTray, bagEntries, isWorn, nextRarity, spareTotal, trayValid } from '../../../../pet/petModel'
import { usePetStore } from '../../../../pet/petStore'
import { Button } from '../../../ui/Button'
import { SettingsGroup } from '../../SettingsGroup'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { PetItemButton } from './PetItemCard'

type Tradeable = Exclude<Rarity, 'legendary'>
const TIERS: Tradeable[] = ['common', 'uncommon', 'rare', 'epic']

const rarityStyle = (id: ItemId): CSSProperties => ({ '--pet-rarity': rarityMeta[itemOf(id).rarity].color } as CSSProperties)

export function PetExchangeGroup({ settings }: { settings: PetSettings }): JSX.Element {
  const t = useT()
  const exchange = usePetStore((state) => state.exchange)
  const [tier, setTier] = useState<Tradeable>('common')
  const [tray, setTray] = useState<ItemId[]>([])
  const { bag, outfit } = settings
  const spare = spareTotal(bag, outfit, tier)
  const target = nextRarity(tier)!
  const valid = trayValid(bag, outfit, tray)
  const rarityLabel = (rarity: Rarity): string => t(`settings.pet.rarity.${rarity}` as MessageKey)
  const nameOf = (id: ItemId): string => t(`pet.item.${id}.name` as MessageKey)
  const options = TIERS.map((value) => ({ value, label: `${rarityLabel(value)} · ${spareTotal(bag, outfit, value)}` }))
  const spares = bagEntries(bag)
    .filter((entry) => itemOf(entry.id).rarity === tier)
    .map((entry) => ({ id: entry.id, copies: entry.count - (isWorn(outfit, entry.id) ? 1 : 0) }))
    .filter((entry) => entry.copies > 0)
    .map((entry) => ({ id: entry.id, left: entry.copies - tray.filter((id) => id === entry.id).length }))
  const full = tray.length >= EXCHANGE_COUNT
  const trade = (): void => {
    const id = exchange(tray)
    if (!id) return
    setTray([])
    showExchangeToast(id)
  }
  return (
    <SettingsGroup title={t('settings.pet.exchange.title')} description={t('settings.pet.exchange.description')} flush>
      <div className="pet-settings-exchange">
        <div className="pet-settings-exchange-head">
          <SegmentedControl<Tradeable> ariaLabel={t('settings.pet.exchange.tier')} value={tier} options={options} onChange={(value) => { setTier(value); setTray([]) }} />
        </div>
        {spares.length > 0
          ? (
            <div className="pet-settings-spares" role="list" aria-label={t('settings.pet.exchange.spares')}>
              {spares.map(({ id, left }) => (
                <PetItemButton key={id} item={id} role="listitem" className="pet-settings-spare" data-rarity={itemOf(id).rarity} style={rarityStyle(id)}
                  disabled={left <= 0 || full} aria-label={t('settings.pet.exchange.add', { name: nameOf(id) })} onClick={() => setTray([...tray, id])}>
                  <DecorationSwatch id={id} size={32} label={nameOf(id)} />
                  <span className="pet-settings-count">×{left}</span>
                </PetItemButton>
              ))}
            </div>
          )
          : <p className="pet-settings-meta">{t('settings.pet.exchange.noSpares')}</p>}
        <div className="pet-settings-tray" role="list" aria-label={t('settings.pet.exchange.tray')}>
          {Array.from({ length: EXCHANGE_COUNT }, (_, index) => {
            const id = tray[index]
            return id
              ? (
                <PetItemButton key={index} item={id} role="listitem" className="pet-settings-tray-slot" data-filled="true" data-rarity={itemOf(id).rarity} style={rarityStyle(id)}
                  aria-label={t('settings.pet.exchange.remove', { name: nameOf(id) })} onClick={() => setTray(tray.filter((_, at) => at !== index))}>
                  <DecorationSwatch id={id} size={36} label={nameOf(id)} />
                </PetItemButton>
              )
              : <span key={index} role="listitem" className="pet-settings-tray-slot" aria-label={t('settings.pet.exchange.empty')} />
          })}
        </div>
        <div className="pet-settings-exchange-actions">
          <Button size="sm" variant="secondary" disabled={spare < EXCHANGE_COUNT} onClick={() => setTray(autoTray(bag, outfit, tier))}>{t('settings.pet.exchange.fill')}</Button>
          <Button size="sm" variant="ghost" disabled={!tray.length} onClick={() => setTray([])}>{t('settings.pet.exchange.clear')}</Button>
          <span className="pet-settings-meta">
            {spare >= EXCHANGE_COUNT && t('settings.pet.exchange.inTray', { count: tray.length, total: EXCHANGE_COUNT })}
          </span>
          <Button variant="primary" disabled={!valid} onClick={trade} aria-label={t('settings.pet.exchange.actionLabel', { rarity: rarityLabel(target) })}>
            {t('settings.pet.exchange.action')}
          </Button>
        </div>
      </div>
    </SettingsGroup>
  )
}
