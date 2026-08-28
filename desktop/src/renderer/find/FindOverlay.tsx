import { useEffect, useRef } from 'react'
import { ChevronDown, ChevronUp, X } from 'lucide-react'
import { useT } from '../contexts/LocaleContext'
import { IconButton } from '../components/ui/IconButton'
import { useFindStore } from '../stores/findStore'
import { subscribeToFindSurfaces } from './registry'
import { useFindDecoration } from './useFindDecoration'
import css from './FindOverlay.module.css'

export function FindOverlay(): JSX.Element | null {
  const t = useT()
  const open = useFindStore((state) => state.open)
  const query = useFindStore((state) => state.query)
  const matches = useFindStore((state) => state.matches)
  const totalMatches = useFindStore((state) => state.totalMatches)
  const isCapped = useFindStore((state) => state.isCapped)
  const activeIndex = useFindStore((state) => state.activeIndex)
  const setQuery = useFindStore((state) => state.setQuery)
  const closeFind = useFindStore((state) => state.closeFind)
  const goToNext = useFindStore((state) => state.goToNext)
  const goToPrevious = useFindStore((state) => state.goToPrevious)
  const refresh = useFindStore((state) => state.refresh)
  const searchNow = useFindStore((state) => state.searchNow)
  const inputRef = useRef<HTMLInputElement>(null)

  useFindDecoration()

  useEffect(() => {
    if (open) inputRef.current?.select()
  }, [open])

  useEffect(() => subscribeToFindSurfaces(refresh), [refresh])

  if (!open) return null

  const hasQuery = query.trim().length > 0
  const counter = !hasQuery
    ? ''
    : matches.length === 0
      ? t('find.noResults')
      : t(isCapped ? 'find.countCapped' : 'find.count', {
          index: activeIndex + 1,
          total: totalMatches
        })

  return (
    <div className={css.overlay} role="search" aria-label={t('find.title')}>
      <input
        ref={inputRef}
        className={css.input}
        type="text"
        value={query}
        autoFocus
        spellCheck={false}
        placeholder={t('find.placeholder')}
        aria-label={t('find.placeholder')}
        onChange={(event) => setQuery(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.preventDefault()
            closeFind()
          } else if (event.key === 'Enter') {
            event.preventDefault()
            // Enter means "go there now", so it outruns the search debounce.
            searchNow()
            if (event.shiftKey) goToPrevious()
            else goToNext()
          }
        }}
      />
      <span className={css.counter} aria-live="polite">{counter}</span>
      <IconButton
        icon={<ChevronUp size={14} strokeWidth={1.8} aria-hidden />}
        label={t('find.previous')}
        tooltipLabel={t('find.previous')}
        size={24}
        radius={6}
        disabled={matches.length === 0}
        onClick={goToPrevious}
      />
      <IconButton
        icon={<ChevronDown size={14} strokeWidth={1.8} aria-hidden />}
        label={t('find.next')}
        tooltipLabel={t('find.next')}
        size={24}
        radius={6}
        disabled={matches.length === 0}
        onClick={goToNext}
      />
      <IconButton
        icon={<X size={14} strokeWidth={1.8} aria-hidden />}
        label={t('find.close')}
        tooltipLabel={t('find.close')}
        size={24}
        radius={6}
        onClick={closeFind}
      />
    </div>
  )
}
