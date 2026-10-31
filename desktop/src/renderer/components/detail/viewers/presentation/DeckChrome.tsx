import type { ReactNode } from 'react'
import { ChevronDown, ChevronLeft, ChevronRight, ChevronUp, LayoutGrid, Play, RectangleHorizontal } from 'lucide-react'
import { useT } from '../../../../contexts/LocaleContext'
import { Button } from '../../../ui/Button'
import { IconButton } from '../../../ui/IconButton'

export function SlideNotes({ notes, open, onToggle }: { notes: string; open: boolean; onToggle: () => void }): JSX.Element {
  const t = useT()
  return (
    <section className="dc-deck__notes" data-open={open || undefined}>
      <button type="button" className="dc-deck__notes-toggle" aria-expanded={open} onClick={onToggle}>
        {open ? <ChevronDown size={14} aria-hidden /> : <ChevronUp size={14} aria-hidden />}
        {t('viewer.slides.notes')}
      </button>
      {open && (
        <div className="dc-deck__notes-body" data-empty={!notes || undefined}>
          {notes || t('viewer.slides.noNotes')}
        </div>
      )}
    </section>
  )
}

export function SlideStepper({ current, total, onStep }: { current: number; total: number; onStep: (index: number) => void }): JSX.Element {
  const t = useT()
  return (
    <div className="dc-deck__stepper" role="group" aria-label={t('viewer.slides.list')}>
      <IconButton
        size={26}
        label={t('viewer.slides.previous')}
        disabled={current <= 0}
        onClick={() => onStep(current - 1)}
        icon={<ChevronLeft size={15} aria-hidden />}
      />
      <span className="dc-deck__position" aria-live="polite">
        {t('viewer.slides.position', { current: current + 1, total })}
      </span>
      <IconButton
        size={26}
        label={t('viewer.slides.next')}
        disabled={current >= total - 1}
        onClick={() => onStep(current + 1)}
        icon={<ChevronRight size={15} aria-hidden />}
      />
    </div>
  )
}

export function DeckFooter({
  leading,
  grid,
  onToggleGrid,
  onPresent,
  zoom
}: {
  leading: ReactNode
  grid: boolean
  onToggleGrid: () => void
  onPresent: () => void
  zoom: ReactNode
}): JSX.Element {
  const t = useT()
  const label = grid ? t('viewer.slides.slideView') : t('viewer.slides.gridView')
  return (
    <div className="dc-deck__footer">
      <div className="dc-deck__footer-leading">{leading}</div>
      <IconButton
        size={26}
        label={label}
        tooltipLabel={label}
        onClick={onToggleGrid}
        icon={grid ? <RectangleHorizontal size={15} aria-hidden /> : <LayoutGrid size={15} aria-hidden />}
      />
      <Button size="sm" variant="ghost" iconLeft={<Play size={14} aria-hidden />} onClick={onPresent}>
        {t('viewer.slides.present')}
      </Button>
      {zoom}
    </div>
  )
}
