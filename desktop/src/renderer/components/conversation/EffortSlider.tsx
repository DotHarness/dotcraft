import { useEffect, useRef, useState, type CSSProperties, type JSX } from 'react'
import { mountCharge, readColor, type ChargeHandle } from './modelPickerCharge'

export interface EffortStop<T extends string> {
  value: T
  label: string
}

const burstParticles = Array.from({ length: 12 }, (_, index) => index)

function reducedMotion(): boolean {
  const configured = document.documentElement.dataset.reduceMotion
  return configured === 'on' || (configured !== 'off' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true)
}

function Charge({ host, fast, charged }: { host: React.RefObject<HTMLDivElement | null>; fast: boolean; charged: boolean }): JSX.Element {
  const canvas = useRef<HTMLCanvasElement>(null)
  const handle = useRef<ChargeHandle | null>(null)
  const [drawn, setDrawn] = useState(false)
  useEffect(() => {
    const element = canvas.current
    const slider = host.current
    if (!element || !slider) return undefined
    const mounted = mountCharge(element, {
      accent: readColor(slider, '--accent', [0.27, 0.4, 0.8]),
      hue: readColor(slider, '--model-picker-hue', [0.6, 0.45, 0.9])
    }, { reducedMotion: reducedMotion() })
    handle.current = mounted
    setDrawn(Boolean(mounted))
    return () => {
      mounted?.dispose()
      handle.current = null
      setDrawn(false)
    }
  }, [host])
  useEffect(() => {
    handle.current?.set({ fast, charged })
  }, [charged, fast])
  return <canvas className="model-picker-slider__gl" ref={canvas} data-drawn={drawn || undefined} aria-hidden />
}

export function EffortSlider<T extends string>({
  stops,
  value,
  ariaLabel,
  fast = false,
  onChange
}: {
  stops: Array<EffortStop<T>>
  value: T
  ariaLabel: string
  fast?: boolean
  onChange: (value: T) => void
}): JSX.Element {
  const root = useRef<HTMLDivElement>(null)
  const current = stops.findIndex((stop) => stop.value === value)
  const top = stops.length > 1 && current === stops.length - 1
  const [burst, setBurst] = useState(0)
  const wasTop = useRef(top)
  useEffect(() => {
    if (top && !wasTop.current) setBurst((count) => count + 1)
    wasTop.current = top
  }, [top])
  const fraction = stops.length > 1 ? current / (stops.length - 1) : 0

  return (
    <div
      className="model-picker-slider"
      style={{ '--model-picker-fraction': fraction } as CSSProperties}
      data-charged={top || undefined}
      data-fast={fast || undefined}
      ref={root}
    >
      <span className="model-picker-slider__track" aria-hidden>
        <span className="model-picker-slider__fill">
          <Charge host={root} fast={fast} charged={top} />
        </span>
      </span>
      <span className="model-picker-slider__dots" aria-hidden>
        {stops.map((stop, index) => <i className={index <= current ? 'is-reached' : undefined} key={stop.value} />)}
      </span>
      <input
        className="model-picker-slider__input"
        type="range"
        min={0}
        max={stops.length - 1}
        step={1}
        value={current}
        aria-label={ariaLabel}
        aria-valuetext={stops[current].label}
        onChange={(event) => onChange(stops[Number(event.target.value)].value)}
      />
      <span className="model-picker-slider__thumb" aria-hidden>
        {burst > 0 && top ? (
          <span className="model-picker-slider__burst" key={burst}>
            {burstParticles.map((index) => <i key={index} />)}
          </span>
        ) : null}
      </span>
    </div>
  )
}
