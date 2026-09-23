import { useState, type CSSProperties, type JSX } from 'react'
import { DecorationSwatch } from '@dotcraft/avatar/react'
import { decorationName, itemOf, rarityMeta, type ItemId, type Rarity } from '@dotcraft/avatar'
import type { MessageKey } from '../../../../../shared/locales'
import type { PetSettings } from '../../../../../shared/pet'
import { useT } from '../../../../contexts/LocaleContext'
import { EXCHANGE_COUNT, autoTray, nextRarity, spareTotal, trayValid } from '../../../../pet/petModel'
import { usePetStore } from '../../../../pet/petStore'
import { Button } from '../../../ui/Button'
import { SettingsGroup } from '../../SettingsGroup'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { PetReveal } from './PetReveal'

type Tradeable = Exclude<Rarity, 'legendary'>
const TIERS: Tradeable[] = ['common', 'uncommon', 'rare', 'epic']

interface PetExchangeGroupProps {
  settings: PetSettings
  onWear: (id: ItemId) => void
}

export function PetExchangeGroup({ settings, onWear }: PetExchangeGroupProps): JSX.Element {
  const t = useT()
  const exchange = usePetStore((state) => state.exchange)
  const [tier, setTier] = useState<Tradeable>('common')
  const [tray, setTray] = useState<ItemId[]>([])
  const [reveal, setReveal] = useState<{ id: ItemId; duplicate: boolean } | null>(null)
  const { bag, outfit } = settings
  const spare = spareTotal(bag, outfit, tier)
  const target = nextRarity(tier)!
  const valid = trayValid(bag, outfit, tray)
  const rarityLabel = (rarity: Rarity): string => t(`settings.pet.rarity.${rarity}` as MessageKey)
  const options = TIERS.map((value) => ({ value, label: `${rarityLabel(value)} · ${spareTotal(bag, outfit, value)}` }))
  const trade = (): void => {
    const id = exchange(tray)
    if (!id) return
    setTray([])
    setReveal({ id, duplicate: (usePetStore.getState().settings.bag[id] ?? 0) > 1 })
  }
  return (
    <SettingsGroup title={t('settings.pet.exchange.title')} description={t('settings.pet.exchange.description')} flush>
      {reveal
        ? <PetReveal id={reveal.id} duplicate={reveal.duplicate} settings={settings} onWear={(id) => { onWear(id); setReveal(null) }} onDismiss={() => setReveal(null)} />
        : (
          <div className="pet-settings-exchange">
            <div className="pet-settings-exchange-head">
              <SegmentedControl<Tradeable> ariaLabel={t('settings.pet.exchange.tier')} value={tier} options={options} onChange={(value) => { setTier(value); setTray([]) }} />
            </div>
            <div className="pet-settings-tray" role="list" aria-label={t('settings.pet.exchange.tray')}>
              {Array.from({ length: EXCHANGE_COUNT }, (_, index) => {
                const id = tray[index]
                return id
                  ? (
                    <button key={index} type="button" role="listitem" className="pet-settings-tray-slot" data-filled="true" data-rarity={itemOf(id).rarity}
                      style={{ '--pet-rarity': rarityMeta[itemOf(id).rarity].color } as CSSProperties}
                      aria-label={t('settings.pet.exchange.remove', { name: decorationName(id) })} onClick={() => setTray(tray.filter((_, at) => at !== index))}>
                      <DecorationSwatch id={id} size={36} />
                    </button>
                  )
                  : <span key={index} role="listitem" className="pet-settings-tray-slot" aria-label={t('settings.pet.exchange.empty')} />
              })}
            </div>
            <div className="pet-settings-exchange-actions">
              <Button size="sm" variant="secondary" disabled={spare < EXCHANGE_COUNT} onClick={() => setTray(autoTray(bag, outfit, tier))}>{t('settings.pet.exchange.fill')}</Button>
              <Button size="sm" variant="ghost" disabled={!tray.length} onClick={() => setTray([])}>{t('settings.pet.exchange.clear')}</Button>
              <span className="pet-settings-meta">
                {spare < EXCHANGE_COUNT
                  ? t('settings.pet.exchange.needed', { count: EXCHANGE_COUNT - spare, rarity: rarityLabel(tier) })
                  : t('settings.pet.exchange.inTray', { count: tray.length, total: EXCHANGE_COUNT })}
              </span>
              <Button variant="primary" disabled={!valid} onClick={trade} aria-label={t('settings.pet.exchange.actionLabel', { rarity: rarityLabel(target) })}>
                {t('settings.pet.exchange.action')}
              </Button>
            </div>
          </div>
        )}
    </SettingsGroup>
  )
}
