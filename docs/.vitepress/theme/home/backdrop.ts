const VERTEX = `
attribute vec2 a_position;
varying vec2 v_uv;
void main() {
  v_uv = a_position * 0.5 + 0.5;
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`

const FRAGMENT = `
precision mediump float;
uniform sampler2D u_image;
uniform vec2 u_cover;
uniform vec2 u_focus;
uniform float u_time;
varying vec2 v_uv;

vec2 flow(vec2 uv, float t) {
  return vec2(
    sin(uv.y * 3.1 + t * 0.23) + sin(uv.x * 1.7 - t * 0.17 + 1.3),
    cos(uv.x * 2.6 - t * 0.19) + cos(uv.y * 1.9 + t * 0.13 + 0.7)
  );
}

void main() {
  vec2 uv = v_uv;
  uv += flow(uv, u_time) * 0.018;
  uv += flow(uv * 1.6 + 4.0, u_time * 0.8) * 0.012;
  vec2 image = uv * u_cover + u_focus * (1.0 - u_cover);
  gl_FragColor = texture2D(u_image, clamp(image, 0.0, 1.0));
}
`

const FRAME_MS = 1000 / 30
const RESOLUTION = 0.5

function compile(gl: WebGLRenderingContext, type: number, source: string): WebGLShader {
  const shader = gl.createShader(type)!
  gl.shaderSource(shader, source)
  gl.compileShader(shader)
  return shader
}

function imageUrl(element: HTMLElement): string | null {
  return /url\("?([^")]+)"?\)/.exec(getComputedStyle(element).backgroundImage)?.[1] ?? null
}

function focus(element: HTMLElement): [number, number] {
  const [x, y] = getComputedStyle(element).backgroundPosition.split(' ').map((part) => parseFloat(part) / 100)
  return [Number.isFinite(x) ? x : 0.5, Number.isFinite(y) ? y : 0.5]
}

export function animateBackdrop(element: HTMLElement): () => void {
  const canvas = document.createElement('canvas')
  const gl = canvas.getContext('webgl', { alpha: false, antialias: false, depth: false, premultipliedAlpha: false })
  if (!gl) return () => {}

  const program = gl.createProgram()!
  gl.attachShader(program, compile(gl, gl.VERTEX_SHADER, VERTEX))
  gl.attachShader(program, compile(gl, gl.FRAGMENT_SHADER, FRAGMENT))
  gl.linkProgram(program)
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) return () => {}
  gl.useProgram(program)

  gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer())
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW)
  const position = gl.getAttribLocation(program, 'a_position')
  gl.enableVertexAttribArray(position)
  gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0)

  const texture = gl.createTexture()
  gl.bindTexture(gl.TEXTURE_2D, texture)
  gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true)
  for (const [key, value] of [[gl.TEXTURE_MIN_FILTER, gl.LINEAR], [gl.TEXTURE_MAG_FILTER, gl.LINEAR], [gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE], [gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE]]) {
    gl.texParameteri(gl.TEXTURE_2D, key, value)
  }

  const cover = gl.getUniformLocation(program, 'u_cover')
  const focusAt = gl.getUniformLocation(program, 'u_focus')
  const time = gl.getUniformLocation(program, 'u_time')

  let aspect = 0
  let source = ''
  let frame = 0
  let last = 0
  let visible = false
  const started = performance.now()

  const fit = (): void => {
    const width = Math.max(1, Math.round(element.clientWidth * RESOLUTION))
    const height = Math.max(1, Math.round(element.clientHeight * RESOLUTION))
    canvas.width = width
    canvas.height = height
    gl.viewport(0, 0, width, height)
    if (!aspect) return
    const box = width / height
    gl.uniform2f(cover, box > aspect ? 1 : box / aspect, box > aspect ? aspect / box : 1)
    gl.uniform2f(focusAt, ...focus(element))
  }

  const draw = (now: number): void => {
    frame = 0
    if (!visible || !aspect) return
    if (now - last >= FRAME_MS) {
      last = now
      gl.uniform1f(time, (now - started) / 1000)
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4)
      canvas.dataset.ready = ''
    }
    frame = requestAnimationFrame(draw)
  }

  const play = (): void => {
    if (!frame && visible && !document.hidden) frame = requestAnimationFrame(draw)
  }

  const load = (): void => {
    const url = imageUrl(element)
    if (!url || url === source) return
    source = url
    const image = new Image()
    image.onload = () => {
      if (source !== url) return
      gl.bindTexture(gl.TEXTURE_2D, texture)
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, image)
      aspect = image.naturalWidth / image.naturalHeight
      fit()
      play()
    }
    image.src = url
  }

  const resize = new ResizeObserver(fit)
  const view = new IntersectionObserver(([entry]) => {
    visible = entry.isIntersecting
    play()
  })
  const theme = new MutationObserver(load)
  const onVisibility = (): void => play()

  element.append(canvas)
  resize.observe(element)
  view.observe(element)
  theme.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
  document.addEventListener('visibilitychange', onVisibility)
  load()

  return () => {
    cancelAnimationFrame(frame)
    resize.disconnect()
    view.disconnect()
    theme.disconnect()
    document.removeEventListener('visibilitychange', onVisibility)
    gl.getExtension('WEBGL_lose_context')?.loseContext()
    canvas.remove()
  }
}
