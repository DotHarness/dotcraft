import { useEffect } from 'react'
import { useT } from '../contexts/LocaleContext'
import { useFindStore } from '../stores/findStore'
import { subscribeToFindSurfaces } from './registry'
import { useFindDecoration } from './useFindDecoration'
import { FindPanel } from './FindPanel'

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
  useFindDecoration()

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

  return <FindPanel label={t('find.title')} placeholder={t('find.placeholder')} query={query}
    counter={counter} hasMatches={matches.length > 0} onChange={setQuery} onClose={closeFind}
    onNext={() => { searchNow(); goToNext() }} onPrevious={() => { searchNow(); goToPrevious() }} />
}
