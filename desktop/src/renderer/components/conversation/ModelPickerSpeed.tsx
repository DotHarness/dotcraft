import { useId, type CSSProperties, type JSX, type MouseEvent as ReactMouseEvent, type Ref } from 'react'
import { Zap } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { InferenceSpeedWire } from '../../stores/modelCatalogStore'
import { IconButton } from '../ui/IconButton'
import { OptionRow, submenuStyle } from './ModelPickerMenu'
import type { AcceleratedSpeed } from './modelSpeed'

type Translate = ReturnType<typeof useT>

interface IconProps {
  className?: string
  size: number
  strokeWidth?: number
  style?: CSSProperties
}

const ZAP_PATH = 'M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z'
const BOLT_SCALE = 0.7
const FRONT_BOLT = `translate(-1.2 6.6) scale(${BOLT_SCALE})`
const BACK_BOLT = `translate(7.2 0.4) scale(${BOLT_SCALE})`
const BOLT_GAP = 1.2

export function UltrafastIcon({ className, size, strokeWidth = 2, style }: IconProps): JSX.Element {
  const mask = useId()
  const stroke = strokeWidth / BOLT_SCALE
  return (
    <svg
      aria-hidden
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="currentColor"
      stroke="currentColor"
      strokeLinecap="round"
      strokeLinejoin="round"
      style={style}
    >
      <mask id={mask} maskUnits="userSpaceOnUse" x="0" y="0" width="24" height="24">
        <rect width="24" height="24" fill="white" stroke="none" />
        <path
          d={ZAP_PATH}
          transform={FRONT_BOLT}
          fill="black"
          stroke="black"
          strokeWidth={(strokeWidth + BOLT_GAP * 2) / BOLT_SCALE}
        />
      </mask>
      <path d={ZAP_PATH} transform={BACK_BOLT} strokeWidth={stroke} mask={`url(#${mask})`} />
      <path d={ZAP_PATH} transform={FRONT_BOLT} strokeWidth={stroke} />
    </svg>
  )
}

export function SpeedIcon({ speed, active, ...props }: IconProps & {
  speed: AcceleratedSpeed
  active: boolean
}): JSX.Element {
  return speed === 'ultrafast'
    ? <UltrafastIcon {...props} />
    : <Zap aria-hidden fill={active ? 'currentColor' : 'none'} {...props} />
}

export function SpeedControl({ advertised, active, flyoutOpen, onChange, onFlyoutHover, onFlyoutOpen }: {
  advertised: AcceleratedSpeed[]
  active: InferenceSpeedWire
  flyoutOpen: boolean
  onChange: (speed: InferenceSpeedWire) => void
  onFlyoutHover: (event: ReactMouseEvent<HTMLButtonElement>) => void
  onFlyoutOpen: (button: HTMLButtonElement) => void
}): JSX.Element {
  const t = useT()
  if (!advertised.includes('ultrafast')) {
    return (
      <IconButton
        size={32}
        className="model-picker-fast"
        aria-pressed={active === 'fast'}
        label={t('composer.speed.fast')}
        tooltipLabel={t('composer.speed.fast')}
        tooltipPlacement="top"
        onClick={() => onChange(active === 'fast' ? 'standard' : 'fast')}
        icon={<SpeedIcon speed="fast" active={active === 'fast'} size={16} />}
      />
    )
  }
  return (
    <IconButton
      size={32}
      className="model-picker-fast"
      data-speed-active={active !== 'standard' || undefined}
      data-submenu="speed"
      aria-haspopup="listbox"
      aria-expanded={flyoutOpen}
      label={`${t('composer.speed.heading')}: ${speedLabel(t, active)}`}
      onMouseEnter={onFlyoutHover}
      onMouseMove={onFlyoutHover}
      onClick={(event) => onFlyoutOpen(event.currentTarget)}
      icon={<SpeedIcon speed={active === 'standard' ? 'fast' : active} active={active !== 'standard'} size={16} />}
    />
  )
}

export function SpeedFlyout({ ref, style, choices, active, highlight, onPointer, onHighlight, onSelect }: {
  ref: Ref<HTMLDivElement>
  style: CSSProperties
  choices: InferenceSpeedWire[]
  active: InferenceSpeedWire
  highlight: number
  onPointer: () => void
  onHighlight: (index: number) => void
  onSelect: (speed: InferenceSpeedWire) => void
}): JSX.Element {
  const t = useT()
  return (
    <div
      ref={ref}
      role="listbox"
      aria-label={t('composer.speed.heading')}
      style={style}
      onMouseEnter={onPointer}
      onMouseMove={onPointer}
    >
      {choices.map((speed, index) => (
        <OptionRow
          key={speed}
          selected={speed === active}
          highlighted={highlight === index}
          label={speedLabel(t, speed)}
          description={speedDescription(t, speed)}
          onHover={() => onHighlight(index)}
          onSelect={() => onSelect(speed)}
        />
      ))}
    </div>
  )
}

export function speedFlyoutStyle(top: number, left: number, maxHeight: number): CSSProperties {
  return {
    ...submenuStyle('speed', top, false, maxHeight),
    left,
    border: '1px solid var(--glass-border)',
    borderRadius: '12px'
  }
}

function speedLabel(t: Translate, speed: InferenceSpeedWire): string {
  switch (speed) {
    case 'standard': return t('composer.speed.standard')
    case 'fast': return t('composer.speed.fast')
    case 'ultrafast': return t('composer.speed.ultrafast')
  }
}

function speedDescription(t: Translate, speed: InferenceSpeedWire): string {
  switch (speed) {
    case 'standard': return t('composer.speed.standardDescription')
    case 'fast': return t('composer.speed.fastDescription')
    case 'ultrafast': return t('composer.speed.ultrafastDescription')
  }
}
