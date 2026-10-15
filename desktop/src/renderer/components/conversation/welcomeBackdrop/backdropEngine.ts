import { THEME_CHANGED_EVENT } from '../../../../shared/theme'
import { buildGlyphAtlas } from './glyphAtlas'
import { BACKDROP_PROGRAMS } from './backdropShaders'
import { createBackdropHand } from './backdropHand'

const CELL_CSS_PX = 6.5
const MIN_COLUMNS = 24
const MIN_ROWS = 8
const MAX_DEVICE_PIXELS = 21e6
const FRAME_MS = 40
const RIPPLE_FRAME_MS = 16
const REVEAL_S = 4
const BUDGET_S = 180
const SETTLE_S = 8
const RESUME_S = 3
const GLYPH_FLICK = 0.35
const GLYPH_FILL = 0.65
const LIT_CONTRAST = { dark: 1.45, light: 1.32 }
const GLOW_HEADROOM = 3
const SOFTWARE_RENDERER = /swiftshader|llvmpipe|softpipe|software|warp|basic render/i
const UNSUPPORTED_DISPLAY = '(prefers-contrast: more), (forced-colors: active)'
const INTERACTIVE = [
  'a,button,input,textarea,select,label,summary',
  "[contenteditable]:not([contenteditable='false'])",
  "[tabindex]:not([tabindex='-1'])",
  ...['button', 'link', 'textbox', 'combobox', 'listbox', 'option', 'menu', 'menuitem', 'tab', 'switch', 'checkbox', 'slider', 'dialog']
    .map((role) => `[role='${role}']`)
].join(',')

const CHARACTER_SETS = ['abcdefghijklmnopqrstuvwxyz0123456789', 'aeiourstnlcdmhpgbfy0123456789']

export interface WelcomeBackdropHandle {
  destroy: () => void
}

interface Grid {
  cellWidth: number
  cellHeight: number
  columns: number
  rows: number
  left: number
  top: number
  scale: number
}

type Rgb = [number, number, number]

function smooth01(value: number): number {
  const t = Math.min(1, Math.max(0, value))
  return t * t * (3 - 2 * t)
}

function seededRandom(seed: number): () => number {
  let state = seed >>> 0
  return () => {
    state = (state + 0x9e3779b9) >>> 0
    let t = state
    t = Math.imul(t ^ (t >>> 16), 0x21f0aaad)
    t = Math.imul(t ^ (t >>> 15), 0x735a2d97)
    return ((t ^ (t >>> 15)) >>> 0) / 4294967296
  }
}

function planGrid(deviceWidth: number, deviceHeight: number, scale: number): Grid | null {
  if (!(deviceWidth > 0 && deviceHeight > 0 && scale > 0)) return null
  const cellWidth = Math.max(5, Math.round(CELL_CSS_PX * scale))
  const cellHeight = cellWidth * 2
  let columns = Math.ceil(deviceWidth / cellWidth)
  let rows = Math.ceil(deviceHeight / cellHeight)
  if (columns < MIN_COLUMNS || rows < MIN_ROWS) return null
  const over = (columns * cellWidth * rows * cellHeight) / MAX_DEVICE_PIXELS
  if (over > 1) {
    columns = Math.floor(columns / Math.sqrt(over))
    rows = Math.floor(rows / Math.sqrt(over))
  }
  return {
    cellWidth,
    cellHeight,
    columns,
    rows,
    left: Math.round((deviceWidth - columns * cellWidth) / 2),
    top: Math.round((deviceHeight - rows * cellHeight) / 2),
    scale
  }
}

function parseColor(doc: Document, css: string, fallback: Rgb): Rgb {
  const ctx = doc.createElement('canvas').getContext('2d', { willReadFrequently: true })
  if (!ctx || !css.trim()) return fallback
  ctx.fillStyle = css
  ctx.fillRect(0, 0, 1, 1)
  const [r = 0, g = 0, b = 0] = ctx.getImageData(0, 0, 1, 1).data
  return [r / 255, g / 255, b / 255]
}

function luminance([r, g, b]: Rgb): number {
  const channel = (c: number): number => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4)
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

function alphaForContrast(ink: Rgb, bg: Rgb, target: number): number {
  const base = luminance(bg)
  const contrast = (alpha: number): number => {
    const mixed = luminance(bg.map((c, i) => c + (ink[i]! - c) * alpha) as Rgb)
    return (Math.max(mixed, base) + 0.05) / (Math.min(mixed, base) + 0.05)
  }
  if (contrast(1) <= target) return 1
  let low = 0
  let high = 1
  for (let i = 0; i < 20; i++) {
    const mid = (low + high) / 2
    if (contrast(mid) < target) low = mid
    else high = mid
  }
  return high
}

function compile(gl: WebGL2RenderingContext, vertex: string, fragment: string): WebGLProgram | null {
  const program = gl.createProgram()
  for (const [type, source] of [[gl.VERTEX_SHADER, vertex], [gl.FRAGMENT_SHADER, fragment]] as const) {
    const shader = gl.createShader(type)
    if (!shader) return null
    gl.shaderSource(shader, source)
    gl.compileShader(shader)
    gl.attachShader(program, shader)
    gl.deleteShader(shader)
  }
  gl.linkProgram(program)
  if (gl.getProgramParameter(program, gl.LINK_STATUS) !== true) {
    gl.deleteProgram(program)
    return null
  }
  return program
}

function createTexture(gl: WebGL2RenderingContext, unit: number): WebGLTexture {
  const texture = gl.createTexture()
  gl.activeTexture(gl.TEXTURE0 + unit)
  gl.bindTexture(gl.TEXTURE_2D, texture)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
  return texture
}

export function startWelcomeBackdrop(
  host: HTMLElement,
  options: {
    still: boolean
    canvasClassName: string
    calm: () => HTMLElement | null
    onShown: () => void
  }
): WelcomeBackdropHandle | null {
  const doc = host.ownerDocument
  const win = doc.defaultView
  if (!win || win.matchMedia(UNSUPPORTED_DISPLAY).matches || typeof win.ResizeObserver !== 'function') return null

  const canvas = doc.createElement('canvas')
  const gl = canvas.getContext('webgl2', {
    alpha: true,
    antialias: false,
    depth: false,
    stencil: false,
    premultipliedAlpha: true,
    preserveDrawingBuffer: false,
    powerPreference: 'low-power',
    failIfMajorPerformanceCaveat: true
  })
  if (!gl) return null
  const debugInfo = gl.getExtension('WEBGL_debug_renderer_info')
  const loseContext = (): void => {
    gl.getExtension('WEBGL_lose_context')?.loseContext()
  }
  if (debugInfo && SOFTWARE_RENDERER.test(String(gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL)))) {
    loseContext()
    return null
  }
  const fieldProgram = compile(gl, BACKDROP_PROGRAMS.vertex, BACKDROP_PROGRAMS.field)
  const glyphProgram = compile(gl, BACKDROP_PROGRAMS.vertex, BACKDROP_PROGRAMS.glyph)
  if (!fieldProgram || !glyphProgram) {
    loseContext()
    return null
  }

  const seed = win.crypto.getRandomValues(new Uint32Array(1))[0] ?? 1
  const random = seededRandom(seed)
  const characters = CHARACTER_SETS[Math.floor(random() * CHARACTER_SETS.length)] ?? CHARACTER_SETS[0]
  const lean = Math.PI / 10 + (Math.PI / 4.3 - Math.PI / 10) * random()
  const heading = random() < 0.5 ? lean : Math.PI - lean
  const between = (low: number, high: number): number => low + (high - low) * random()
  const wind = [heading, between(42, 64), between(380, 560), between(120, 190)] as const

  const fieldTexture = createTexture(gl, 0)
  const atlasTexture = createTexture(gl, 1)
  const sequenceTexture = createTexture(gl, 2)
  const framebuffer = gl.createFramebuffer()
  const vertexArray = gl.createVertexArray()
  gl.bindVertexArray(vertexArray)
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1)

  const at = (program: WebGLProgram, name: string): WebGLUniformLocation | null => gl.getUniformLocation(program, name)
  const field = {
    seed: at(fieldProgram, 'u_seed'),
    time: at(fieldProgram, 'u_time'),
    now: at(fieldProgram, 'u_now'),
    reveal: at(fieldProgram, 'u_reveal'),
    cellCss: at(fieldProgram, 'u_cellCss'),
    paneCss: at(fieldProgram, 'u_paneCss'),
    calm: at(fieldProgram, 'u_calm'),
    wind: at(fieldProgram, 'u_wind'),
    pointer: at(fieldProgram, 'u_pointer'),
    tapCount: at(fieldProgram, 'u_tapCount'),
    taps: at(fieldProgram, 'u_taps')
  }
  const glyph = {
    count: at(glyphProgram, 'u_count'),
    bufferHeight: at(glyphProgram, 'u_bufferHeight'),
    cell: at(glyphProgram, 'u_cell'),
    grid: at(glyphProgram, 'u_grid'),
    time: at(glyphProgram, 'u_time'),
    strength: at(glyphProgram, 'u_strength'),
    glowStrength: at(glyphProgram, 'u_glowStrength'),
    ink: at(glyphProgram, 'u_ink'),
    glow: at(glyphProgram, 'u_glow')
  }
  gl.useProgram(fieldProgram)
  gl.uniform1ui(field.seed, seed)
  gl.uniform4f(field.wind, ...wind)
  gl.useProgram(glyphProgram)
  gl.uniform1i(at(glyphProgram, 'u_field'), 0)
  gl.uniform1i(at(glyphProgram, 'u_atlas'), 1)
  gl.uniform1i(at(glyphProgram, 'u_sequence'), 2)
  gl.uniform1ui(at(glyphProgram, 'u_seed'), seed)
  gl.uniform1f(at(glyphProgram, 'u_flick'), GLYPH_FLICK)
  gl.uniform1f(at(glyphProgram, 'u_fill'), GLYPH_FILL)

  canvas.className = options.canvasClassName
  host.append(canvas)

  let grid: Grid | null = null
  let atlasCellWidth = 0
  let pane = { width: 0, height: 0 }
  let destroyed = false
  let shown = false
  const hand = createBackdropHand()
  const startedAt = win.performance.now()
  const now = (): number => (win.performance.now() - startedAt) / 1000
  let active = 0
  let time = (seed / 4294967296) * 64
  let lastActivity = 0
  let resumedAt = -RESUME_S
  let lastTick = 0
  let timer: number | undefined
  let frame: number | undefined

  const applyColors = (): void => {
    const style = win.getComputedStyle(host)
    const ink = parseColor(doc, style.color, [0.5, 0.5, 0.5])
    const bg = parseColor(doc, style.getPropertyValue('--bg-primary'), [0.5, 0.5, 0.5])
    const glow = parseColor(doc, style.getPropertyValue('--accent'), [0.2, 0.5, 1])
    const strength = alphaForContrast(ink, bg, luminance(bg) < 0.18 ? LIT_CONTRAST.dark : LIT_CONTRAST.light)
    gl.useProgram(glyphProgram)
    gl.uniform3f(glyph.ink, ...ink)
    gl.uniform3f(glyph.glow, ...glow)
    gl.uniform1f(glyph.strength, strength)
    gl.uniform1f(glyph.glowStrength, Math.min(0.85, strength * GLOW_HEADROOM))
  }

  const resize = (deviceWidth: number, deviceHeight: number, cssWidth: number, cssHeight: number): boolean => {
    const next = planGrid(deviceWidth, deviceHeight, win.devicePixelRatio)
    canvas.style.visibility = next ? '' : 'hidden'
    if (!next) return false
    if (next.cellWidth !== atlasCellWidth) {
      const atlas = buildGlyphAtlas(doc, win.getComputedStyle(host).fontFamily, { width: next.cellWidth, height: next.cellHeight }, next.scale, characters)
      if (!atlas) return false
      gl.activeTexture(gl.TEXTURE1)
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.R8, atlas.width, atlas.height, 0, gl.RED, gl.UNSIGNED_BYTE, atlas.coverage)
      gl.activeTexture(gl.TEXTURE2)
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.R8, atlas.sequence.length, 1, 0, gl.RED, gl.UNSIGNED_BYTE, atlas.sequence)
      gl.useProgram(glyphProgram)
      gl.uniform1i(glyph.count, atlas.sequence.length)
      atlasCellWidth = next.cellWidth
    }
    const width = next.columns * next.cellWidth
    const height = next.rows * next.cellHeight
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width
      canvas.height = height
    }
    const css = (devicePx: number): number => devicePx / next.scale
    canvas.style.left = `${css(next.left)}px`
    canvas.style.top = `${css(next.top)}px`
    canvas.style.width = `${css(width)}px`
    canvas.style.height = `${css(height)}px`
    if (!grid || grid.columns !== next.columns || grid.rows !== next.rows) {
      gl.activeTexture(gl.TEXTURE0)
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, next.columns, next.rows, 0, gl.RGBA, gl.UNSIGNED_BYTE, null)
      gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer)
      gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, fieldTexture, 0)
    }
    grid = next
    pane = { width: cssWidth, height: cssHeight }
    gl.useProgram(fieldProgram)
    gl.uniform2f(field.cellCss, css(next.cellWidth), css(next.cellHeight))
    gl.uniform2f(field.paneCss, cssWidth, cssHeight)
    gl.useProgram(glyphProgram)
    gl.uniform2i(glyph.cell, next.cellWidth, next.cellHeight)
    gl.uniform2i(glyph.grid, next.columns, next.rows)
    gl.uniform1i(glyph.bufferHeight, height)
    return true
  }

  const calmRect = (): [number, number, number, number] => {
    const element = options.calm()
    const hostRect = host.getBoundingClientRect()
    if (!element) return [pane.width / 2, pane.height * 0.45, 260, 120]
    const rect = element.getBoundingClientRect()
    return [
      rect.left - hostRect.left + rect.width / 2,
      rect.top - hostRect.top + rect.height / 2,
      Math.max(rect.width / 2, 120),
      Math.max(rect.height / 2, 60)
    ]
  }

  const draw = (energy: number): void => {
    if (!grid || gl.isContextLost()) return
    const clock = now()
    const touch = options.still ? null : hand.frame(clock)
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer)
    gl.viewport(0, 0, grid.columns, grid.rows)
    gl.useProgram(fieldProgram)
    gl.uniform1f(field.time, time)
    gl.uniform1f(field.now, clock)
    gl.uniform1f(field.reveal, options.still ? 1 : smooth01(active / REVEAL_S) * Math.max(energy, 0.35))
    gl.uniform4f(field.calm, ...calmRect())
    gl.uniform3f(field.pointer, ...(touch?.pointer ?? [0, 0, 0]))
    gl.uniform1i(field.tapCount, touch?.tapCount ?? 0)
    if (touch && touch.tapCount > 0) gl.uniform4fv(field.taps, touch.taps)
    gl.drawArrays(gl.TRIANGLES, 0, 3)

    gl.bindFramebuffer(gl.FRAMEBUFFER, null)
    gl.viewport(0, 0, canvas.width, canvas.height)
    gl.clearColor(0, 0, 0, 0)
    gl.clear(gl.COLOR_BUFFER_BIT)
    gl.useProgram(glyphProgram)
    gl.uniform1f(glyph.time, time)
    gl.drawArrays(gl.TRIANGLES, 0, 3)
    if (!shown) {
      shown = true
      options.onShown()
    }
  }

  const energyAt = (): number =>
    (1 - smooth01((active - lastActivity - BUDGET_S) / SETTLE_S)) * smooth01((active - resumedAt) / RESUME_S)
  const canRun = (): boolean => !destroyed && !options.still && grid !== null && !doc.hidden && doc.hasFocus()

  const stop = (): void => {
    win.clearTimeout(timer)
    if (frame !== undefined) win.cancelAnimationFrame(frame)
    timer = undefined
    frame = undefined
  }

  const tick = (): void => {
    frame = undefined
    const clock = now()
    const dt = Math.min(Math.max(clock - lastTick, 0), 0.25)
    lastTick = clock
    active += dt
    const energy = energyAt()
    time += dt * energy
    draw(energy)
    if (energy > 0 || hand.busy(clock)) schedule()
  }

  const schedule = (): void => {
    if (!canRun() || timer !== undefined || frame !== undefined) return
    timer = win.setTimeout(() => {
      timer = undefined
      frame = win.requestAnimationFrame(tick)
    }, hand.rippling(now()) ? RIPPLE_FRAME_MS : FRAME_MS)
  }

  const resume = (): void => {
    if (!canRun()) {
      stop()
      return
    }
    if (timer === undefined && frame === undefined) {
      lastTick = now()
      schedule()
    }
  }

  const wake = (): void => {
    if (energyAt() <= 0) resumedAt = active
    lastActivity = active
    resume()
  }

  const parent = host.parentElement
  const local = (event: PointerEvent): [number, number] => {
    const rect = host.getBoundingClientRect()
    return [event.clientX - rect.left, event.clientY - rect.top]
  }
  const onPointerMove = (event: PointerEvent): void => {
    if (event.pointerType !== 'mouse') return
    hand.move(...local(event), now())
    wake()
  }
  const onPointerDown = (event: PointerEvent): void => {
    if (event.button !== 0) return
    const target = event.target instanceof Element ? event.target.closest(INTERACTIVE) : null
    if (!target || target === parent || !parent?.contains(target)) hand.tap(...local(event), now())
    wake()
  }
  const onKeyDown = (): void => wake()

  const observer = new win.ResizeObserver((entries) => {
    const entry = entries[0]
    if (!entry) return
    const device = entry.devicePixelContentBoxSize?.[0]
    const box = entry.contentBoxSize?.[0]
    const cssWidth = box?.inlineSize ?? entry.contentRect.width
    const cssHeight = box?.blockSize ?? entry.contentRect.height
    const deviceWidth = device?.inlineSize ?? Math.round(cssWidth * win.devicePixelRatio)
    const deviceHeight = device?.blockSize ?? Math.round(cssHeight * win.devicePixelRatio)
    if (resize(deviceWidth, deviceHeight, cssWidth, cssHeight)) {
      draw(energyAt())
      resume()
    }
  })
  try {
    observer.observe(host, { box: 'device-pixel-content-box' })
  } catch {
    observer.observe(host)
  }

  const recolor = (): void => {
    applyColors()
    draw(energyAt())
  }
  const themeObserver = new win.MutationObserver(recolor)
  themeObserver.observe(doc.documentElement, { attributes: true, attributeFilter: ['data-theme', 'style'] })
  const darkQuery = win.matchMedia('(prefers-color-scheme: dark)')
  darkQuery.addEventListener('change', recolor)
  win.addEventListener(THEME_CHANGED_EVENT, recolor)
  doc.addEventListener('visibilitychange', resume)
  win.addEventListener('focus', resume)
  win.addEventListener('blur', resume)
  if (!options.still) {
    parent?.addEventListener('pointermove', onPointerMove, { passive: true })
    parent?.addEventListener('pointerdown', onPointerDown, { passive: true })
    parent?.addEventListener('pointerleave', hand.leave, { passive: true })
    parent?.addEventListener('keydown', onKeyDown, { capture: true, passive: true })
  }
  applyColors()

  const destroy = (): void => {
    if (destroyed) return
    destroyed = true
    stop()
    observer.disconnect()
    themeObserver.disconnect()
    darkQuery.removeEventListener('change', recolor)
    win.removeEventListener(THEME_CHANGED_EVENT, recolor)
    doc.removeEventListener('visibilitychange', resume)
    win.removeEventListener('focus', resume)
    win.removeEventListener('blur', resume)
    parent?.removeEventListener('pointermove', onPointerMove)
    parent?.removeEventListener('pointerdown', onPointerDown)
    parent?.removeEventListener('pointerleave', hand.leave)
    parent?.removeEventListener('keydown', onKeyDown, { capture: true })
    canvas.removeEventListener('webglcontextlost', destroy)
    gl.deleteProgram(fieldProgram)
    gl.deleteProgram(glyphProgram)
    gl.deleteTexture(fieldTexture)
    gl.deleteTexture(atlasTexture)
    gl.deleteTexture(sequenceTexture)
    gl.deleteFramebuffer(framebuffer)
    gl.deleteVertexArray(vertexArray)
    loseContext()
    canvas.remove()
  }
  canvas.addEventListener('webglcontextlost', destroy)
  return { destroy }
}
