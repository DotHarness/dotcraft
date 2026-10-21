import { useLayoutEffect, useState, type ButtonHTMLAttributes, type CSSProperties, type FocusEvent, type JSX, type MouseEvent } from 'react'
import { createPortal } from 'react-dom'
import { itemOf, rarityMeta, type ItemId } from '@dotcraft/avatar'
import type { MessageKey } from '../../../../../shared/locales'
import { useT } from '../../../../contexts/LocaleContext'
import { useTransientOverlay } from '../../../../hooks/useTransientOverlay'
import { DETAILS_CARD_CLOSE_DELAY_MS, DETAILS_CARD_OPEN_DELAY_MS } from '../../../sidebar/SidebarEntryDetailsCard'

const CARD_WIDTH = 240
const GAP = 8
const VIEWPORT_PADDING = 8

interface PetItemButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  item: ItemId
}

export function PetItemButton({ item: id, onMouseEnter, onMouseLeave, onFocus, onBlur, children, ...props }: PetItemButtonProps): JSX.Element {
  const t = useT()
  const { visible, anchorRef, overlayRef, open, scheduleOpen, scheduleClose, hide } = useTransientOverlay<HTMLButtonElement, HTMLDivElement>({
    openDelayMs: DETAILS_CARD_OPEN_DELAY_MS,
    closeDelayMs: DETAILS_CARD_CLOSE_DELAY_MS
  })
  const [position, setPosition] = useState({ left: 0, top: 0 })

  useLayoutEffect(() => {
    if (!visible) return
    const anchor = anchorRef.current?.getBoundingClientRect()
    if (!anchor) return
    const height = overlayRef.current?.offsetHeight ?? 0
    const left = Math.min(Math.max(anchor.left + anchor.width / 2 - CARD_WIDTH / 2, VIEWPORT_PADDING), window.innerWidth - CARD_WIDTH - VIEWPORT_PADDING)
    const above = anchor.top - height - GAP
    setPosition({ left, top: above >= VIEWPORT_PADDING ? above : anchor.bottom + GAP })
  }, [visible, id, anchorRef, overlayRef])

  const item = itemOf(id)
  const style = { left: position.left, top: position.top, width: CARD_WIDTH, '--pet-rarity': rarityMeta[item.rarity].color } as CSSProperties
  return (
    <>
      <button
        type="button"
        {...props}
        ref={anchorRef}
        onMouseEnter={(event: MouseEvent<HTMLButtonElement>) => { scheduleOpen(); onMouseEnter?.(event) }}
        onMouseLeave={(event: MouseEvent<HTMLButtonElement>) => { scheduleClose(); onMouseLeave?.(event) }}
        onFocus={(event: FocusEvent<HTMLButtonElement>) => { open(); onFocus?.(event) }}
        onBlur={(event: FocusEvent<HTMLButtonElement>) => { hide(); onBlur?.(event) }}
      >
        {children}
      </button>
      {visible && createPortal(
        <div ref={overlayRef} role="tooltip" className="pet-item-card" style={style}>
          <span className="pet-item-card-name">{t(`pet.item.${id}.name` as MessageKey)}</span>
          <span className="pet-settings-rarity-word">{t(`settings.pet.rarity.${item.rarity}` as MessageKey)}</span>
          <span className="pet-item-card-feature">
            {t(`settings.pet.slot.${item.slot}` as MessageKey)} · {t(`pet.item.${id}.feature` as MessageKey)}
          </span>
        </div>,
        document.body
      )}
    </>
  )
}
