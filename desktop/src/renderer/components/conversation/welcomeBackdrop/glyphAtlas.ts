const ATLAS_CHARACTERS = ' !"#$%&\'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·'
const ATLAS_COLUMNS = 16
const ATLAS_ROWS = 6

export interface GlyphCell {
  width: number
  height: number
}

export interface GlyphAtlas {
  coverage: Uint8Array
  width: number
  height: number
  sequence: Uint8Array
}

const GLYPH_CSS_PX = 9
const GLYPH_WEIGHT = 300

export function buildGlyphAtlas(
  doc: Document,
  family: string,
  cell: GlyphCell,
  devicePixelRatio: number,
  characters: string
): GlyphAtlas | null {
  const canvas = doc.createElement('canvas')
  canvas.width = ATLAS_COLUMNS * cell.width
  canvas.height = ATLAS_ROWS * cell.height
  const ctx = canvas.getContext('2d', { willReadFrequently: true })
  if (!ctx) return null

  // Thin strokes vanish at 1x, so low-density screens draw one weight heavier.
  const weight = devicePixelRatio >= 1.5 ? GLYPH_WEIGHT : GLYPH_WEIGHT + 100
  ctx.font = `${weight} 100px ${family}`
  const advanceRatio = ctx.measureText('0').width / 100 || 0.6
  const px = Math.min(Math.round(GLYPH_CSS_PX * devicePixelRatio), Math.floor((cell.width - 2) / advanceRatio))
  ctx.font = `${weight} ${px}px ${family}`
  ctx.textAlign = 'left'
  ctx.textBaseline = 'alphabetic'
  ctx.fillStyle = 'white'

  const metrics = ctx.measureText('Mg|')
  const ascent = metrics.fontBoundingBoxAscent
  const baseline = Math.round((cell.height - (ascent + metrics.fontBoundingBoxDescent)) / 2 + ascent)
  const advance = ctx.measureText('0').width
  const inset = Math.floor((cell.width - advance) / 2)
  const drawn = new Set<number>()
  ;[...ATLAS_CHARACTERS].forEach((char, index) => {
    if (Math.abs(ctx.measureText(char).width - advance) > 0.5) return
    const x = (index % ATLAS_COLUMNS) * cell.width
    const y = Math.floor(index / ATLAS_COLUMNS) * cell.height
    ctx.save()
    ctx.beginPath()
    ctx.rect(x, y, cell.width, cell.height)
    ctx.clip()
    ctx.fillText(char, x + inset, y + baseline)
    ctx.restore()
    drawn.add(index)
  })

  const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height).data
  const coverage = new Uint8Array(canvas.width * canvas.height)
  for (let i = 0; i < coverage.length; i++) coverage[i] = pixels[i * 4 + 3] ?? 0

  const indices = [...characters]
    .map((char) => ATLAS_CHARACTERS.indexOf(char))
    .filter((index) => index > 0 && drawn.has(index))
  const fallback = ATLAS_CHARACTERS.indexOf('.')
  return {
    coverage,
    width: canvas.width,
    height: canvas.height,
    sequence: Uint8Array.from(indices.length > 0 ? indices : [fallback])
  }
}
