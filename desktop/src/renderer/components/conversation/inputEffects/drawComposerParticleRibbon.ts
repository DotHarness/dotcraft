import type { ThemeVariant } from '../../../../shared/themeSeed'

export const VISIBLE_ENERGY = 0.017

const COLUMN_STEP = 3.7
const ROW_STEP = 3.1
const PALETTE = {
  dark: [[116, 122, 255], [68, 178, 255], [81, 249, 213], [156, 134, 250], [255, 104, 207]],
  light: [[64, 76, 216], [8, 107, 218], [0, 148, 151], [109, 69, 214], [193, 48, 151]],
}

function interpolateColor(x: number, theme: ThemeVariant): string {
  const colors = PALETTE[theme]
  const position = x * (colors.length - 1)
  const index = Math.min(colors.length - 2, Math.floor(position))
  const fraction = position - index
  return `rgb(${colors[index].map((value, channel) =>
    Math.round(value + (colors[index + 1][channel] - value) * fraction)).join(' ')})`
}

const COLORS = {
  dark: Array.from({ length: 101 }, (_, index) => interpolateColor(index / 100, 'dark')),
  light: Array.from({ length: 101 }, (_, index) => interpolateColor(index / 100, 'light')),
}

export function colorAt(x: number, theme: ThemeVariant): string {
  return COLORS[theme][Math.round(Math.max(0, Math.min(x, 1)) * 100)]
}

function lobe(distance: number, width: number): number {
  return Math.exp(-(distance * distance) / (width * width))
}

function bloom(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  color: string,
  opacity: number
) {
  context.save()
  context.translate(x, y)
  context.scale(width, height)
  const gradient = context.createRadialGradient(0, 0, 0, 0, 0, 1)
  gradient.addColorStop(0, color)
  gradient.addColorStop(1, 'transparent')
  context.globalAlpha = opacity
  context.fillStyle = gradient
  context.fillRect(-1, -1, 2, 2)
  context.restore()
}

export function drawRibbon(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  { energy, time: t }: { energy: number; time: number },
  theme: ThemeVariant
) {
  if (energy <= VISIBLE_ENERGY) return

  const span = width * 0.86
  const columns = Math.max(2, Math.floor(span / COLUMN_STEP))
  const start = (width - span) / 2
  const bottom = height - 0.6
  const high = 0.51 + 0.18 * Math.sin(t * 1.5)
  const low = 0.64 + 0.17 * Math.cos(t * 1.85 + 0.8)
  const light = theme === 'light'

  bloom(context, start + high * span, bottom - 3, span * 0.28, 26, colorAt(high, theme), energy * (light ? 0.17 : 0.43))
  bloom(context, start + (low * 0.45 + 0.43) * span, bottom - 1, span * 0.22, 19, colorAt(0.94, theme), energy * (light ? 0.13 : 0.36))

  for (let column = 0; column <= columns; column++) {
    const x = column / columns
    const fade = Math.sin(Math.PI * x) ** 0.95
    const primary = energy * (0.12 + 0.86 * lobe(x - high, 0.20) + 0.43 * lobe(x - low + 0.13, 0.095))
    const secondary = energy * (0.12 + 0.91 * lobe(x - low, 0.22))
    const baseX = start + x * span

    for (const rear of [true, false]) {
      const activity = Math.tanh((rear ? secondary : primary) * 1.25)
      const drift = t * (rear ? 5.2 : -3.8)
      const ripple = 0.84 + 0.16 * Math.sin(x * (rear ? 23 : 32) - drift)
      const crest = fade * (2.2 + activity * ripple * (rear ? 48 : 42))
      const ridge = Math.floor(crest / ROW_STEP)

      for (let row = 0; row <= ridge; row++) {
        const depth = row * ROW_STEP / Math.max(crest, 1)
        const texture = Math.sin(column * 25.13 + row * 17.7 + Number(rear) * 4.2)
        const size = (rear ? 1.25 : 1.75) * (1 - depth * 0.28)
        const shear = Math.sin(x * 9 + t * 2.8) * row * (rear ? 0.92 : -0.47)
        const edgeFade = Math.min(1, (crest - row * ROW_STEP + 0.8) / ROW_STEP)
        context.globalAlpha = Math.min(1, (0.78 + texture * 0.2) * fade
          * (0.20 * energy + 1.22 * activity) * (rear ? 0.70 : 1) * (1 - depth * 0.33)) * edgeFade
        const hue = 0.5 + (x - 0.5) * 1.85 + (rear ? 0.16 + 0.06 * depth : -0.048 * depth)
        context.fillStyle = colorAt(hue, theme)
        context.fillRect(baseX + (row % 2) * 1.75 + shear, bottom - row * ROW_STEP - size - (rear ? 1.5 : 0), size, size)
      }
    }
  }

  context.globalAlpha = 1
}

const EXIT_TRAILS = Array.from({ length: 20 }, (_, index) => ({
  from: 0.19 + 0.57 * index / 19,
  delay: 0.03 + ((index * 11) % 19) / 18 * 0.22,
  duration: 0.53 + ((index * 13) % 19) / 18 * 0.22,
  lift: 2 + ((index * 7 + 1) % 13),
  length: 6 + ((index * 5 + 2) % 15),
  drift: 0.45 + ((index * 3) % 11) * 0.19
}))

export interface RibbonExitPoint {
  x: number
  y: number
}

function trailPoint(
  trail: typeof EXIT_TRAILS[number],
  age: number,
  width: number,
  height: number,
  target: RibbonExitPoint
): RibbonExitPoint {
  const from = width * trail.from
  const travel = age ** 1.17
  const liftPhase = Math.max(0, (age - 0.48) / 0.52)
  const lift = liftPhase ** 2 * (3 - 2 * liftPhase)
  const low = height - trail.lift * (1 - 0.38 * age) - Math.sin(Math.PI * age) * trail.drift
  return {
    x: from + (target.x - from) * travel,
    y: low + (target.y - low) * lift
  }
}

export function drawRibbonExit(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  progress: number,
  voice: { energy: number; time: number },
  target: RibbonExitPoint,
  theme: ThemeVariant
): void {
  const remainingRibbon = Math.max(0, 1 - progress / 0.24)
  if (remainingRibbon > 0) {
    drawRibbon(context, width, height, {
      energy: voice.energy * remainingRibbon ** 1.45,
      time: voice.time
    }, theme)
  }

  const tailScale = Math.min(1.2, Math.max(0.66, width / 540))
  for (const trail of EXIT_TRAILS) {
    const age = (progress - trail.delay) / trail.duration
    if (age <= 0 || age >= 1) continue

    const head = trailPoint(trail, age, width, height, target)
    const back = trailPoint(trail, Math.max(0, age - 0.045), width, height, target)
    const dx = head.x - back.x
    const dy = head.y - back.y
    const distance = Math.hypot(dx, dy) || 1
    const light = Math.min(1, age * 11) * (1 - age ** 4) ** 0.9 * (0.61 + voice.energy * 0.39)
    const tail = trail.length * tailScale * (0.5 + 0.5 * Math.sin(Math.PI * age))
    const step = tail / 5
    context.fillStyle = colorAt((trail.from - 0.13) / 0.64, theme)
    for (let segment = 0; segment < 5; segment++) {
      const u = (segment + 1) / 5
      const offset = tail - step * segment
      context.globalAlpha = light * u * 0.65
      context.fillRect(
        head.x - dx / distance * offset,
        head.y - dy / distance * offset,
        Math.max(0.65, step * Math.abs(dx / distance)),
        0.62 + u * 0.26
      )
    }
    context.globalAlpha = light
    context.fillRect(head.x + 0.65, head.y - 0.23, 1.4, 1.25)
  }
  context.globalAlpha = 1
}
