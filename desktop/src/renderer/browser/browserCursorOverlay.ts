import type { BrowserHostCursor } from '../../shared/viewer/browserHost'
import { prefersReducedMotion } from '../utils/appearance'
import { CursorMotion } from './cursorMotion'
import { clamp, distanceBetween, normalizeDegrees, REST_TIP_ANGLE, type Point, type Size } from './cursorPath'
import { shortestTurn } from './cursorSpring'

export interface BrowserCursorView {
  shown: boolean
  size: Size
  cursor?: BrowserHostCursor
}

export interface BrowserCursorOverlay {
  update(view: BrowserCursorView): void
  destroy(): void
}

const SVG_NAMESPACE = 'http://www.w3.org/2000/svg'
const IDLE_X = 0.58
const IDLE_Y = 0.55
const HIDDEN_SCALE = 0.4
const HIDDEN_BLUR = 5
const SNAP_DISTANCE = 0.5
const FIRST_FRAME = 1 / 60

function createArt(): SVGSVGElement {
  const art = document.createElementNS(SVG_NAMESPACE, 'svg')
  const outline = document.createElementNS(SVG_NAMESPACE, 'path')
  art.setAttribute('class', 'dc-browser-cursor-art')
  art.setAttribute('viewBox', '0 0 28 28')
  outline.setAttribute('d', 'M4.7 3.9 21.8 14l-8.2 1.7L9.5 23.3 4.7 3.9Z')
  art.append(outline)
  return art
}

function restrict(point: Point, size: Size): Point {
  return { x: clamp(point.x, 0, size.width), y: clamp(point.y, 0, size.height) }
}

function round(value: number): number {
  return Math.round(value * 1000) / 1000
}

export function createBrowserCursorOverlay(
  parent: HTMLElement,
  onArrived: (moveSequence: number) => void
): BrowserCursorOverlay {
  const layer = document.createElement('div')
  const mark = document.createElement('div')
  layer.className = 'dc-browser-cursor'
  layer.setAttribute('aria-hidden', 'true')
  layer.hidden = true
  mark.className = 'dc-browser-cursor-mark'
  mark.append(createArt())
  layer.append(mark)
  parent.append(layer)

  let motion: CursorMotion | undefined
  let shown = false
  let destination: Point | undefined
  let pending: number | undefined
  let handled: number | undefined
  let frame: number | undefined
  let lastFrame: number | undefined

  function snap(cursor: CursorMotion, target: Point): void {
    cursor.snapTo(target)
    destination = target
    pending = undefined
  }

  function arrive(sequence: number | undefined): void {
    if (sequence !== undefined) onArrived(sequence)
  }

  function render(now: number): void {
    if (!motion) return
    const pose = motion.pose(now)
    const visibility = clamp(pose.visibility, 0, 1)
    const scale = HIDDEN_SCALE + (1 - HIDDEN_SCALE) * visibility
    const squash = clamp(pose.scootSquash, 0, 1)
    const transform = [`translate3d(${round(pose.point.x)}px, ${round(pose.point.y)}px, 0)`]
    if (Math.abs(shortestTurn(0, pose.scootAxis)) > 0.001 || Math.abs(squash - 1) > 0.001) {
      transform.push(`rotate(${round(pose.scootAxis)}deg)`, `scale(1, ${round(squash)})`, `rotate(${round(-pose.scootAxis)}deg)`)
    }
    transform.push(
      `rotate(${round(normalizeDegrees(pose.tipAngle - REST_TIP_ANGLE + pose.scootTilt))}deg)`,
      `scale(${round(pose.stretch * scale)}, ${round(scale)})`
    )
    mark.style.transform = transform.join(' ')
    mark.style.opacity = String(round(visibility))
    mark.style.filter = `blur(${round(HIDDEN_BLUR * (1 - visibility))}px)`
    layer.hidden = !shown && visibility <= 0.001
  }

  function tick(time: number): void {
    frame = undefined
    if (!motion) return
    const arrived = motion.step(lastFrame === undefined ? FIRST_FRAME : (time - lastFrame) / 1000, time)
    lastFrame = time
    render(time)
    if (arrived && pending !== undefined) {
      const sequence = pending
      pending = undefined
      onArrived(sequence)
    }
    if (motion.active) frame = requestAnimationFrame(tick)
    else lastFrame = undefined
  }

  function update(view: BrowserCursorView): void {
    const { cursor, size } = view
    const position = cursor?.x !== undefined && cursor.y !== undefined ? { x: cursor.x, y: cursor.y } : undefined
    const sequence = cursor?.moveSequence !== undefined && cursor.moveSequence !== handled ? cursor.moveSequence : undefined
    if (sequence !== undefined) handled = sequence
    if (!motion && !view.shown) {
      arrive(sequence)
      return
    }
    const target = restrict(position ?? { x: Math.round(size.width * IDLE_X), y: Math.round(size.height * IDLE_Y) }, size)
    const current = motion ??= new CursorMotion(target, false)
    const appearing = view.shown && !shown
    if (view.shown !== shown) {
      shown = view.shown
      current.setVisible(shown, shown || prefersReducedMotion())
    }
    if (appearing) {
      snap(current, target)
      if (!position && !prefersReducedMotion()) current.startWobble(performance.now())
      arrive(sequence)
    } else if (
      view.shown && sequence !== undefined && position && cursor?.animate !== false &&
      distanceBetween(current.position, target) >= SNAP_DISTANCE && !prefersReducedMotion()
    ) {
      pending = sequence
      destination = target
      current.glideTo(target, size)
    } else if (sequence !== undefined || (view.shown && destination && distanceBetween(destination, target) >= SNAP_DISTANCE)) {
      snap(current, target)
      arrive(sequence)
    }
    render(performance.now())
    if (frame === undefined && current.active) frame = requestAnimationFrame(tick)
  }

  function destroy(): void {
    if (frame !== undefined) cancelAnimationFrame(frame)
    frame = undefined
    layer.remove()
  }

  return { update, destroy }
}
