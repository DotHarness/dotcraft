import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { ArrowDownAZ, ArrowDownZA } from 'lucide-react'
import { useT } from '../../../../contexts/LocaleContext'
import { Checkbox } from '../../../ui/Checkbox'
import type { SortDirection } from './sheetFilter'

const MENU_WIDTH = 240
const MAX_VALUES = 500

export function FilterMenu({
  anchor,
  values,
  excluded,
  sort,
  onExclude,
  onSort,
  onClose
}: {
  anchor: DOMRect
  values: string[]
  excluded: string[]
  sort: SortDirection | null
  onExclude: (excluded: string[]) => void
  onSort: (direction: SortDirection) => void
  onClose: () => void
}): JSX.Element {
  const t = useT()
  const ref = useRef<HTMLDivElement>(null)
  const hidden = new Set(excluded)
  const shown = values.filter((value) => !hidden.has(value)).length

  useEffect(() => {
    const onPointer = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) onClose()
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      event.stopPropagation()
      onClose()
    }
    document.addEventListener('pointerdown', onPointer, true)
    document.addEventListener('keydown', onKey, true)
    ref.current?.querySelector<HTMLElement>('button')?.focus()
    return () => {
      document.removeEventListener('pointerdown', onPointer, true)
      document.removeEventListener('keydown', onKey, true)
    }
  }, [onClose])

  const left = Math.max(8, Math.min(anchor.right - MENU_WIDTH, window.innerWidth - MENU_WIDTH - 8))
  const top = Math.min(anchor.bottom + 4, window.innerHeight - 160)
  const toggle = (value: string, checked: boolean) =>
    onExclude(checked ? excluded.filter((entry) => entry !== value) : [...excluded, value])

  return createPortal(
    <div
      ref={ref}
      className="dc-sheet-filter"
      role="dialog"
      aria-label={t('viewer.sheet.filterSort')}
      style={{ left, top, width: MENU_WIDTH, maxHeight: window.innerHeight - top - 8 }}
    >
      <button type="button" className="dc-sheet-filter__sort" aria-pressed={sort === 'asc'} onClick={() => onSort('asc')}>
        <ArrowDownAZ size={16} aria-hidden />
        {t('viewer.sheet.sortAscending')}
      </button>
      <button type="button" className="dc-sheet-filter__sort" aria-pressed={sort === 'desc'} onClick={() => onSort('desc')}>
        <ArrowDownZA size={16} aria-hidden />
        {t('viewer.sheet.sortDescending')}
      </button>
      <div className="dc-sheet-filter__separator" role="separator" />
      <div className="dc-sheet-filter__values">
        <Checkbox
          checked={shown === values.length}
          indeterminate={shown > 0 && shown < values.length}
          onChange={(checked) => onExclude(checked ? [] : [...values])}
          label={t('viewer.sheet.selectAll')}
        />
        {values.slice(0, MAX_VALUES).map((value) => (
          <Checkbox
            key={value}
            checked={!hidden.has(value)}
            onChange={(checked) => toggle(value, checked)}
            label={<span className="dc-sheet-filter__label">{value || t('viewer.sheet.blank')}</span>}
          />
        ))}
      </div>
    </div>,
    document.body
  )
}
