import { useEffect, useRef, useState, type CSSProperties, type JSX } from 'react'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ProviderMark } from '../ui/ProviderMark'
import type { BrandPartner } from './useBrandPartner'
import css from './SidebarBrand.module.css'

type Beat = 'hop' | 'pop' | 'boot' | 'spin'

const STREAK_GAP_MS = 900
const WORDMARK = 'DotCraft'
const SPARKS = 7

function beatFor(streak: number): Beat {
  if (streak >= 10) return 'spin'
  if (streak >= 5) return 'boot'
  if (streak >= 3) return 'pop'
  return 'hop'
}

function BrandMark({ beat, seq }: { beat: Beat | null; seq: number }): JSX.Element {
  return (
    <span key={seq} className={css.mark} data-beat={beat ?? undefined} aria-hidden="true">
      <svg className={css.markSvg} width={18} height={18} viewBox="0 0 24 24" fill="currentColor" overflow="visible">
        <g className={css.ball}>
          <circle cx="12" cy="4" r="2" />
          <circle className={css.bulb} cx="12" cy="4" r="2" fill="#ffc40c" />
        </g>
        <path d="M11.25,5.5h1.5v2.5h-1.5z" />
        <path d="M2.5,12h1.5v4h-1.5z M20,12h1.5v4h-1.5z" />
        <path
          fillRule="evenodd"
          clipRule="evenodd"
          d="M7.5,8h9a3.5,3.5 0,0 1,3.5 3.5v5a3.5,3.5 0,0 1,-3.5 3.5h-9a3.5,3.5 0,0 1,-3.5 -3.5v-5a3.5,3.5 0,0 1,3.5 -3.5z M7.6,11.1l1.1,-1.1l3.9,3.9l-3.9,3.9l-1.1,-1.1l2.8,-2.8z M13.5,16.4h3.5v1.5h-3.5z"
        />
        <path className={css.cursor} d="M13.5,16.4h3.5v1.5h-3.5z" />
      </svg>
      {beat === 'boot' ? (
        <span className={css.sparks}>
          {Array.from({ length: SPARKS }, (_, index) => (
            <span key={index} className={css.spark} style={{ ['--i' as string]: index }} />
          ))}
        </span>
      ) : null}
    </span>
  )
}

interface InkBox { x: number; y: number; w: number; h: number }

const inkBoxes = new Map<string, InkBox | null>()
const INK_SAMPLE = 96

function measureInk(src: string): Promise<InkBox | null> {
  return new Promise((resolve) => {
    const image = new Image()
    image.onload = () => {
      const canvas = document.createElement('canvas')
      canvas.width = INK_SAMPLE
      canvas.height = INK_SAMPLE
      const context = canvas.getContext('2d', { willReadFrequently: true })
      if (!context) return resolve(null)
      context.drawImage(image, 0, 0, INK_SAMPLE, INK_SAMPLE)
      let data: Uint8ClampedArray
      try {
        data = context.getImageData(0, 0, INK_SAMPLE, INK_SAMPLE).data
      } catch {
        return resolve(null)
      }
      let left = INK_SAMPLE, top = INK_SAMPLE, right = -1, bottom = -1
      for (let y = 0; y < INK_SAMPLE; y++) {
        for (let x = 0; x < INK_SAMPLE; x++) {
          if (data[(y * INK_SAMPLE + x) * 4 + 3] < 24) continue
          if (x < left) left = x
          if (x > right) right = x
          if (y < top) top = y
          if (y > bottom) bottom = y
        }
      }
      if (right < 0) return resolve(null)
      resolve({ x: left / INK_SAMPLE, y: top / INK_SAMPLE, w: (right - left + 1) / INK_SAMPLE, h: (bottom - top + 1) / INK_SAMPLE })
    }
    image.onerror = () => resolve(null)
    image.src = src
  })
}

function useInkBox(src: string): InkBox | null | undefined {
  const [box, setBox] = useState<InkBox | null | undefined>(() => inkBoxes.get(src))
  useEffect(() => {
    if (inkBoxes.has(src)) {
      setBox(inkBoxes.get(src))
      return
    }
    let live = true
    void measureInk(src).then((measured) => {
      inkBoxes.set(src, measured)
      if (live) setBox(measured)
    })
    return () => { live = false }
  }, [src])
  return box
}

function PartnerIcon({ src, size }: { src: string; size: number }): JSX.Element {
  const box = useInkBox(src)
  const maxWidth = size
  const maxHeight = size * 0.78
  let style: CSSProperties = { width: size, height: size, visibility: box === undefined ? 'hidden' : undefined }
  if (box) {
    const scale = Math.min(maxWidth / box.w, maxHeight / box.h)
    style = {
      width: scale,
      height: scale,
      maxWidth: 'none',
      transform: `translate(${size / 2 - (box.x + box.w / 2) * scale}px, ${size / 2 - (box.y + box.h / 2) * scale}px)`
    }
  }
  return (
    <span className={css.partnerIconFrame} style={{ width: size, height: size }}>
      <img className={css.partnerIcon} src={src} alt="" draggable={false} style={style} />
    </span>
  )
}

function PartnerMark({ partner, size }: { partner: BrandPartner; size: number }): JSX.Element {
  if (partner.iconSrc) return <PartnerIcon src={partner.iconSrc} size={size} />
  if (partner.markKind) return <ProviderMark kind={partner.markKind} size={Math.round(size * 0.78)} />
  return <span className={css.partnerInitial}>{partner.name.slice(0, 1)}</span>
}

export function SidebarBrand({ partner }: { partner: BrandPartner | null }): JSX.Element {
  const [beat, setBeat] = useState<{ kind: Beat; seq: number } | null>(null)
  const streak = useRef({ count: 0, at: 0 })
  const previousPartner = useRef<string | null | undefined>(undefined)
  const [linkSeq, setLinkSeq] = useState(0)

  const partnerKey = partner?.key ?? null
  useEffect(() => {
    const previous = previousPartner.current
    previousPartner.current = partnerKey
    if (previous !== null || partnerKey === null) return
    setLinkSeq((value) => value + 1)
    setBeat((current) => ({ kind: 'pop', seq: (current?.seq ?? 0) + 1 }))
  }, [partnerKey])

  const onPointerDown = (): void => {
    const now = performance.now()
    const run = streak.current
    run.count = now - run.at <= STREAK_GAP_MS ? run.count + 1 : 1
    run.at = now
    const kind = beatFor(run.count)
    if (kind === 'spin') run.count = 0
    setBeat((current) => ({ kind, seq: (current?.seq ?? 0) + 1 }))
  }

  return (
    <span className={css.brand}>
      <span className={css.self} onPointerDown={onPointerDown}>
        <BrandMark beat={beat?.kind ?? null} seq={beat?.seq ?? 0} />
        <span key={beat?.kind === 'boot' ? beat.seq : 0} className={css.wordmark} data-wave={beat?.kind === 'boot' ? true : undefined}>
          {Array.from(WORDMARK, (letter, index) => (
            <span key={index} className={css.letter} style={{ ['--i' as string]: index }}>{letter}</span>
          ))}
        </span>
      </span>
      {partner ? (
        <span key={`${partner.key}:${linkSeq}`} className={css.partner} data-link={linkSeq > 0 ? true : undefined}>
          <span className={css.rule} aria-hidden="true" />
          <ActionTooltip label={partner.name} placement="bottom" wrapperClassName={css.partnerMark}>
            <span role="img" aria-label={partner.name} className={css.partnerMarkInner}>
              <PartnerMark partner={partner} size={18} />
            </span>
          </ActionTooltip>
        </span>
      ) : null}
    </span>
  )
}
