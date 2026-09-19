interface ChargeColors {
  accent: [number, number, number]
  hue: [number, number, number]
}

interface ChargeState {
  fast: boolean
  charged: boolean
}

export interface ChargeHandle {
  set(state: ChargeState): void
  dispose(): void
}

const vertex = `
attribute vec2 a_pos;
void main() { gl_Position = vec4(a_pos, 0.0, 1.0); }
`

const fragment = `
precision mediump float;
uniform vec2 u_res;
uniform float u_dpr;
uniform float u_time;
uniform float u_phase;
uniform float u_reveal;
uniform float u_fast;
uniform float u_charged;
uniform vec3 u_accent;
uniform vec3 u_hue;
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
float vnoise(vec2 p) {
  vec2 i = floor(p), f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
  return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
float fbm(vec2 p) {
  float v = 0.0, a = 0.5;
  for (int i = 0; i < 3; i++) { v += a * vnoise(p); p = p * 2.03 + 7.1; a *= 0.5; }
  return v;
}
void main() {
  vec2 uv = gl_FragCoord.xy / u_res;
  float aspect = u_res.x / u_res.y;
  float t = u_time;
  vec2 p = vec2(uv.x * aspect, uv.y);
  float n = fbm(vec2(p.x * 1.2 - t * 0.06, p.y * 1.1 + t * 0.02));
  float cloud = smoothstep(0.25, 0.85, fbm(vec2(p.x * 2.4 + t * 0.04, p.y * 2.0 - t * 0.03) + n));
  float frontier = 1.0 - u_reveal;
  float arrival = smoothstep(frontier - 0.18, frontier + 0.06, uv.x) * u_charged;
  float bloom = smoothstep(0.08, 0.9, uv.x) * (0.62 + 0.38 * cloud);
  vec3 col = mix(u_accent, u_hue, bloom * arrival);
  col = mix(col, vec3(1.0), smoothstep(0.72, 1.0, uv.x) * 0.2 * cloud * arrival);
  float presence = max(u_fast, arrival);
  vec2 sp = vec2(gl_FragCoord.x / u_dpr + u_phase, gl_FragCoord.y / u_dpr);
  vec2 cellId = floor(sp / 8.0);
  vec2 cellUv = fract(sp / 8.0);
  float h = hash(cellId);
  float star = 0.0;
  if (h > 0.85) {
    vec2 center = vec2(hash(cellId + 1.3), hash(cellId + 2.7)) * 0.6 + 0.2;
    float d = length(cellUv - center) * 8.0;
    float twinkle = 0.55 + 0.45 * sin(t * (2.0 + h * 5.0) + h * 40.0);
    star = smoothstep(1.4, 0.3, d) * twinkle;
  }
  col += star * presence * 0.9;
  gl_FragColor = vec4(col, 1.0);
}
`

const smooth = (edge0: number, edge1: number, value: number): number => {
  const x = Math.min(1, Math.max(0, (value - edge0) / (edge1 - edge0)))
  return x * x * (3 - 2 * x)
}

interface Level {
  from: number
  to: number
  at: number
}

const levelAt = (level: Level, now: number): number => level.from + (level.to - level.from) * smooth(0, 1, (now - level.at) / 360)

function compile(gl: WebGLRenderingContext, type: number, source: string): WebGLShader | null {
  const shader = gl.createShader(type)
  if (!shader) return null
  gl.shaderSource(shader, source)
  gl.compileShader(shader)
  return shader
}

function gamma(channel: number): number {
  const value = Math.min(1, Math.max(0, channel))
  return value <= 0.0031308 ? 12.92 * value : 1.055 * Math.pow(value, 1 / 2.4) - 0.055
}

function oklchToSrgb(lightness: number, chroma: number, hueDegrees: number): [number, number, number] {
  const hue = (hueDegrees * Math.PI) / 180
  const a = chroma * Math.cos(hue)
  const b = chroma * Math.sin(hue)
  const l = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (lightness - 0.0894841775 * a - 1.291485548 * b) ** 3
  return [
    gamma(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
    gamma(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
    gamma(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s)
  ]
}

/** Reads a colour the way the element would paint it, so `color-mix()`, `oklch()` and `var()` all resolve. */
export function readColor(element: Element, property: string, fallback: [number, number, number]): [number, number, number] {
  const probe = document.createElement('span')
  probe.style.color = `var(${property})`
  probe.style.position = 'absolute'
  probe.style.visibility = 'hidden'
  element.appendChild(probe)
  const value = getComputedStyle(probe).color
  probe.remove()
  const rgb = /rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)/.exec(value)
  if (rgb) return [Number(rgb[1]) / 255, Number(rgb[2]) / 255, Number(rgb[3]) / 255]
  const oklch = /oklch\(\s*([\d.]+)(%?)\s+([\d.]+)\s+([\d.]+)/.exec(value)
  if (oklch) return oklchToSrgb(Number(oklch[1]) / (oklch[2] ? 100 : 1), Number(oklch[3]), Number(oklch[4]))
  return fallback
}

export function mountCharge(
  canvas: HTMLCanvasElement,
  colors: ChargeColors,
  options: { reducedMotion: boolean }
): ChargeHandle | null {
  const gl = canvas.getContext('webgl', { alpha: false, antialias: false, depth: false, stencil: false })
  if (!gl) return null
  const vs = compile(gl, gl.VERTEX_SHADER, vertex)
  const fs = compile(gl, gl.FRAGMENT_SHADER, fragment)
  const program = gl.createProgram()
  if (!vs || !fs || !program) return null
  gl.attachShader(program, vs)
  gl.attachShader(program, fs)
  gl.linkProgram(program)
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) return null
  gl.useProgram(program)

  const buffer = gl.createBuffer()
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer)
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW)
  const position = gl.getAttribLocation(program, 'a_pos')
  gl.enableVertexAttribArray(position)
  gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0)

  const uniform = (name: string): WebGLUniformLocation | null => gl.getUniformLocation(program, name)
  const u = {
    res: uniform('u_res'),
    dpr: uniform('u_dpr'),
    time: uniform('u_time'),
    phase: uniform('u_phase'),
    reveal: uniform('u_reveal'),
    fast: uniform('u_fast'),
    charged: uniform('u_charged'),
    accent: uniform('u_accent'),
    hue: uniform('u_hue')
  }
  gl.uniform3fv(u.accent, colors.accent)
  gl.uniform3fv(u.hue, colors.hue)

  const startedAt = performance.now()
  let chargedAt = startedAt
  let state: ChargeState = { fast: false, charged: false }
  let fast: Level = { from: 0, to: 0, at: startedAt }
  let charged: Level = { from: 0, to: 0, at: startedAt }
  let phase = 0
  let previous = startedAt
  const draw = (now: number): void => {
    const fastLevel = levelAt(fast, now)
    phase += (6 + 22 * fastLevel) * Math.max(0, now - previous) / 1000
    previous = now
    gl.uniform1f(u.time, (now - startedAt) / 1000)
    gl.uniform1f(u.phase, phase)
    gl.uniform1f(u.reveal, options.reducedMotion ? 1 : smooth(0, 1, (now - chargedAt) / 800))
    gl.uniform1f(u.fast, fastLevel)
    gl.uniform1f(u.charged, levelAt(charged, now))
    gl.drawArrays(gl.TRIANGLES, 0, 3)
  }

  const dpr = Math.min(window.devicePixelRatio || 1, 2)
  const resize = (): void => {
    const width = Math.max(1, Math.round(canvas.clientWidth * dpr))
    const height = Math.max(1, Math.round(canvas.clientHeight * dpr))
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width
      canvas.height = height
    }
    gl.viewport(0, 0, width, height)
    gl.uniform2f(u.res, width, height)
    gl.uniform1f(u.dpr, dpr)
    // A resized canvas is blank until it is drawn, so it is drawn now rather than at the next frame.
    draw(performance.now())
  }
  const observer = new ResizeObserver(resize)
  observer.observe(canvas)
  resize()


  let frame = 0
  let last = 0
  const loop = (now: number): void => {
    frame = requestAnimationFrame(loop)
    if (document.hidden || now - last < 33) return
    if (fast.to === 0 && charged.to === 0 && now - Math.max(fast.at, charged.at) > 400) return
    last = now
    draw(now)
  }
  if (!options.reducedMotion) frame = requestAnimationFrame(loop)

  return {
    set(next: ChargeState): void {
      const now = performance.now()
      // Reaching the top is an arrival, so the hue's reveal starts over each time it is reached.
      if (next.charged && !state.charged) chargedAt = now
      if (next.fast !== state.fast) fast = { from: levelAt(fast, now), to: next.fast ? 1 : 0, at: now }
      if (next.charged !== state.charged) charged = { from: levelAt(charged, now), to: next.charged ? 1 : 0, at: now }
      state = next
      if (options.reducedMotion) {
        fast = { ...fast, from: fast.to }
        charged = { ...charged, from: charged.to }
        draw(now)
      }
    },
    dispose(): void {
      cancelAnimationFrame(frame)
      observer.disconnect()
      // A remounted canvas keeps its context, so only one that has left the document gives it back.
      if (!canvas.isConnected) gl.getExtension('WEBGL_lose_context')?.loseContext()
    }
  }
}
