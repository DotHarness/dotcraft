import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { flushSync } from 'react-dom'
import type { FileRevealRequest } from '../../../../../shared/viewer/types'
import { elementLabels, type ReadPresentationResult } from '../../../../../shared/viewer/presentation'
import { useT } from '../../../../contexts/LocaleContext'
import { FindPanel } from '../../../../find/FindPanel'
import { useFindStore } from '../../../../stores/findStore'
import { useUIStore } from '../../../../stores/uiStore'
import type { PreviewBox } from '../PreviewComments'
import { ViewerZoomControl } from '../ViewerZoomControl'
import { clampZoom } from '../viewerZoom'
import { DeckFooter, SlideNotes, SlideStepper } from './DeckChrome'
import { SlideShow } from './SlideShow'
import { SlideStage, type StageHighlight } from './SlideStage'
import { SlideGrid, SlideRail } from './SlideThumbnails'
import { findSlideMatches, firstMatchFrom, fitScale, NARROW_DECK_WIDTH, scaleFrame, slideForKey, stepMatchIndex } from './slideNavigation'
import { useSlideComments, type SlideSelection } from './useSlideComments'

const LINK_SCHEMES = /^(https?:|mailto:)/i
const SLIDE_ACTION_INSET = 40

interface PresentationDeckProps {
  absolutePath: string
  deck: ReadPresentationResult
  reveal: FileRevealRequest | null
  onRevealed: (revision: number) => void
}

export function PresentationDeck({ absolutePath, deck, reveal, onRevealed }: PresentationDeckProps): JSX.Element {
  const t = useT()
  const railPreferred = useUIStore((state) => state.slideSidebarVisible)
  const rootRef = useRef<HTMLDivElement>(null)
  const stageRef = useRef<HTMLDivElement | null>(null)
  const showRef = useRef<HTMLDivElement | null>(null)
  const [deckWidth, setDeckWidth] = useState(Number.POSITIVE_INFINITY)
  const [current, setCurrent] = useState(0)
  const [view, setView] = useState<'slide' | 'grid'>('slide')
  const [zoom, setZoom] = useState<number | null>(null)
  const [viewport, setViewport] = useState({ width: 0, height: 0 })
  const [selection, setSelection] = useState<SlideSelection | null>(null)
  const [notesOpen, setNotesOpen] = useState(true)
  const [presenting, setPresenting] = useState(false)
  const [finding, setFinding] = useState(false)
  const [query, setQuery] = useState('')
  const [matchIndex, setMatchIndex] = useState(-1)

  const count = deck.slides.length
  const index = Math.min(current, count - 1)
  const slide = deck.slides[index]!
  const narrow = deckWidth < NARROW_DECK_WIDTH
  const showRail = railPreferred && !narrow && view === 'slide'
  const padding = narrow ? 12 : 24
  const fit = fitScale(viewport, deck, padding)
  const fitRef = useRef(fit)
  fitRef.current = fit
  const scale = zoom ?? fit
  const labels = useMemo(() => elementLabels(slide), [slide])

  useLayoutEffect(() => {
    const element = rootRef.current
    if (!element) return
    const observer = new ResizeObserver(() => setDeckWidth(element.clientWidth))
    observer.observe(element)
    setDeckWidth(element.clientWidth)
    return () => observer.disconnect()
  }, [])

  const slideBox = (): PreviewBox => ({ top: padding, left: padding, width: deck.width * scale, height: deck.height * scale })
  const boxFor = (target: SlideSelection): PreviewBox => {
    const element = target.elementId ? slide.elements.find((entry) => entry.id === target.elementId) : undefined
    if (element) return scaleFrame(element.frame, scale, padding)
    const box = slideBox()
    return { ...box, height: Math.max(0, box.height - SLIDE_ACTION_INSET) }
  }

  const comments = useSlideComments({
    absolutePath,
    slide: view === 'slide' ? slide : undefined,
    labels,
    selection,
    showAction: !presenting,
    boxFor,
    visible: { left: 0, right: Math.max(deck.width * scale + padding * 2, viewport.width) }
  })
  const commentsRef = useRef(comments)
  commentsRef.current = comments

  const go = useCallback(
    (next: number) => {
      setCurrent(Math.min(count - 1, Math.max(0, next)))
      setSelection(null)
      commentsRef.current.openCard(null)
    },
    [count]
  )

  const matches = useMemo(() => (finding ? findSlideMatches(deck.slides, query) : []), [finding, deck.slides, query])
  const activeMatch = matches[matchIndex] ?? null
  const currentRef = useRef(index)
  currentRef.current = index
  useEffect(() => {
    if (!finding) return
    const first = firstMatchFrom(matches, currentRef.current)
    setMatchIndex(first)
    if (first >= 0) {
      setCurrent(matches[first]!.slide)
      setView('slide')
    }
  }, [finding, matches])

  useEffect(() => {
    if (!finding) return
    useFindStore.getState().closeFind()
    return useFindStore.subscribe((state, previous) => {
      if (state.open && !previous.open) setFinding(false)
    })
  }, [finding])

  useEffect(() => {
    if (!reveal) return
    const number = reveal.location.slide
    const target = number ? deck.slides.findIndex((entry) => entry.number === number) : -1
    if (number && target >= 0) {
      const elementId = reveal.targetKind === 'element' ? reveal.location.elementId ?? null : null
      const exists = !elementId || deck.slides[target]!.elements.some((entry) => entry.id === elementId)
      const place = { slide: number, elementId: exists ? elementId : null }
      setView('slide')
      setCurrent(target)
      setSelection(place)
      commentsRef.current.openCard(commentsRef.current.findSaved({ slide: number, elementId })?.id ?? null)
    }
    stageRef.current?.focus({ preventScroll: true })
    onRevealed(reveal.revision)
  }, [reveal, deck.slides, onRevealed])

  const onZoom = useCallback((update: (value: number) => number) => setZoom((value) => clampZoom(update(value ?? fitRef.current))), [])
  const onViewport = useCallback((size: { width: number; height: number }) => {
    setViewport((previous) => (previous.width === size.width && previous.height === size.height ? previous : size))
  }, [])
  const onExitShow = useCallback(
    (last: number) => {
      setPresenting(false)
      go(last)
      requestAnimationFrame(() => stageRef.current?.focus({ preventScroll: true }))
    },
    [go]
  )

  const step = (direction: 1 | -1) => {
    const next = stepMatchIndex(matchIndex, matches.length, direction)
    setMatchIndex(next)
    if (next >= 0) {
      setCurrent(matches[next]!.slide)
      setView('slide')
    }
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.defaultPrevented || presenting) return
    const target = event.target instanceof HTMLElement ? event.target : null
    if (target?.closest('input, textarea, [contenteditable="true"]')) return
    const mod = event.ctrlKey || event.metaKey
    if (mod && !event.shiftKey && !event.altKey && event.key.toLowerCase() === 'f') {
      event.preventDefault()
      setFinding(true)
      return
    }
    if (event.key === 'Escape') {
      setSelection(null)
      comments.openCard(null)
      return
    }
    if (mod || event.altKey) return
    const next = slideForKey(event.key, index, count)
    if (next === null) return
    event.preventDefault()
    go(next)
  }

  const highlights: StageHighlight[] = matches.flatMap((match, position) => {
    if (match.slide !== index) return []
    const element = slide.elements.find((entry) => entry.id === match.elementId)
    return element ? [{ key: `${match.elementId}:${position}`, box: scaleFrame(element.frame, scale, padding), active: match === activeMatch }] : []
  })
  const selectedElement = selection?.slide === slide.number && selection.elementId ? slide.elements.find((entry) => entry.id === selection.elementId) : undefined
  const openSlide = (target: number) => {
    go(target)
    setView('slide')
  }

  return (
    <div ref={rootRef} className="dc-deck__layout" data-viewer-find onKeyDown={onKeyDown}>
      <div className="dc-deck__main">
        {showRail && <SlideRail deck={deck} current={index} counts={comments.counts} onOpen={go} />}
        {view === 'grid' ? (
          <SlideGrid deck={deck} current={index} counts={comments.counts} onOpen={openSlide} />
        ) : (
          <div className="dc-deck__body">
            <SlideStage
              slide={slide}
              width={deck.width}
              height={deck.height}
              images={deck.images}
              scale={scale}
              padding={padding}
              label={t('viewer.slides.slide', { number: slide.number })}
              slideSelected={selection?.slide === slide.number && selection.elementId === null}
              selectionBox={selectedElement ? scaleFrame(selectedElement.frame, scale, padding) : null}
              highlights={highlights}
              overlays={comments.overlays}
              onViewport={onViewport}
              onZoom={onZoom}
              onSelect={(next) => {
                comments.openCard(null)
                setSelection(next)
              }}
              onLink={(url) => {
                if (LINK_SCHEMES.test(url)) void window.api.shell.openExternal(url).catch(() => undefined)
              }}
              focusRef={(element) => {
                stageRef.current = element
              }}
            />
            <SlideNotes notes={slide.notes} open={notesOpen} onToggle={() => setNotesOpen((open) => !open)} />
          </div>
        )}
      </div>
      <DeckFooter
        leading={showRail ? null : <SlideStepper current={index} total={count} onStep={go} />}
        grid={view === 'grid'}
        onToggleGrid={() => setView((mode) => (mode === 'grid' ? 'slide' : 'grid'))}
        onPresent={() => {
          flushSync(() => setPresenting(true))
          void showRef.current?.requestFullscreen().catch(() => undefined)
        }}
        zoom={view === 'slide' ? <ViewerZoomControl zoom={scale} onZoom={setZoom} onFit={() => setZoom(null)} fitLabel={t('viewer.slides.fit')} /> : null}
      />
      {presenting && <SlideShow deck={deck} start={index} rootRef={showRef} onExit={onExitShow} />}
      {finding && (
        <FindPanel
          label={t('find.title')}
          placeholder={t('find.placeholder')}
          query={query}
          hasMatches={matches.length > 0}
          counter={matches.length ? `${matchIndex + 1} / ${matches.length}` : t('find.noResults')}
          onChange={setQuery}
          onNext={() => step(1)}
          onPrevious={() => step(-1)}
          onClose={() => {
            setFinding(false)
            setQuery('')
            stageRef.current?.focus({ preventScroll: true })
          }}
        />
      )}
    </div>
  )
}
