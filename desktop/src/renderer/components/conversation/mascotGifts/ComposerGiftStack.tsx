import { useEffect, type CSSProperties } from 'react'
import { DecorationSwatch } from '@dotcraft/avatar/react'
import { itemOf, rarityMeta, type ItemId } from '@dotcraft/avatar'
import { useT } from '../../../contexts/LocaleContext'
import type { MessageKey } from '../../../../shared/locales'
import { registerGiftStage, useGiftStore } from '../../../pet/giftStore'
import { usePetStore } from '../../../pet/petStore'
import { Button } from '../../ui/Button'
import styles from './ComposerGiftStack.module.css'

const SHOWN = 3
const arrived = new Set<number>()

function rarityStyle(id: ItemId): CSSProperties {
  return { '--gift-rarity': rarityMeta[itemOf(id).rarity].color } as CSSProperties
}

function GiftBox() {
  return (
    <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden className={styles.box}>
      <g className={styles.lid}>
        <path d="M10 6.2C8.6 3.4 5.4 2.6 5.2 4.6 5.1 6 7.6 6.4 10 6.2Z" className={styles.bow} />
        <path d="M10 6.2c1.4-2.8 4.6-3.6 4.8-1.6.1 1.4-2.4 1.8-4.8 1.6Z" className={styles.bow} />
        <rect x="2" y="6" width="16" height="4.2" rx="1.2" className={styles.lidFill} />
        <rect x="8.6" y="6" width="2.8" height="4.2" className={styles.ribbon} />
      </g>
      <rect x="3" y="10.2" width="14" height="8.6" rx="1.6" className={styles.body} />
      <rect x="8.6" y="10.2" width="2.8" height="8.6" className={styles.ribbon} />
    </svg>
  )
}

function GiftReveal({ id, onKeep, onWear }: { id: ItemId; onKeep: () => void; onWear: () => void }) {
  const t = useT()
  const name = t(`pet.item.${id}.name` as MessageKey)
  const rarity = itemOf(id).rarity
  return (
    <div role="status" className={styles.reveal} style={rarityStyle(id)}>
      <div className={styles.revealRow}>
        <span className={styles.art} data-rarity={rarity}>
          <span className={styles.artBurst} aria-hidden />
          <span className={styles.artItem}><DecorationSwatch id={id} size={34} label={name} /></span>
        </span>
        <span className={styles.copy}>
          <span className={styles.title}>{t('pet.find.title', { item: name })}</span>
          <span className={styles.rarity}>{t(`settings.pet.rarity.${rarity}` as MessageKey)}</span>
        </span>
      </div>
      <div className={styles.actions}>
        <Button size="sm" variant="secondary" onClick={onKeep}>{t('pet.gift.keep')}</Button>
        <Button size="sm" variant="primary" onClick={onWear}>{t('pet.find.wear')}</Button>
      </div>
    </div>
  )
}

export function ComposerGiftStack() {
  const t = useT()
  const gifts = useGiftStore((state) => state.gifts)
  const opened = useGiftStore((state) => state.opened)
  const open = useGiftStore((state) => state.open)
  const dismiss = useGiftStore((state) => state.dismiss)
  const wear = usePetStore((state) => state.wear)
  useEffect(() => registerGiftStage(), [])
  useEffect(() => {
    for (const gift of gifts) arrived.add(gift.key)
  }, [gifts])

  const shown = gifts.slice(-SHOWN)
  const more = gifts.length - shown.length
  return (
    <div className={styles.stack}>
      {shown.map((gift, index) => (
        <button
          key={gift.key}
          type="button"
          className={styles.gift}
          data-rarity={itemOf(gift.id).rarity}
          data-arriving={arrived.has(gift.key) ? undefined : 'true'}
          style={rarityStyle(gift.id)}
          aria-label={t('pet.gift.open')}
          onClick={() => open(gift.key)}
        >
          <GiftBox />
          {index === 0 && more > 0 && <span className={styles.more}>+{more}</span>}
        </button>
      ))}
      {opened && (
        <GiftReveal
          key={opened.key}
          id={opened.id}
          onKeep={dismiss}
          onWear={() => {
            wear(opened.id)
            dismiss()
          }}
        />
      )}
    </div>
  )
}
