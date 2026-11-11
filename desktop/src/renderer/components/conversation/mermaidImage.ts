const IMAGE_PADDING = 24

export function mermaidSvgToImageSrc(svgMarkup: string, background: string): string | null {
  const template = document.createElement('template')
  template.innerHTML = svgMarkup
  const svg = template.content.querySelector('svg')
  const box = svg?.getAttribute('viewBox')?.trim().split(/[\s,]+/).map(Number)
  if (!svg || !box || box.length !== 4 || box.some((value) => !Number.isFinite(value)) || box[2] <= 0 || box[3] <= 0) {
    return null
  }

  const [x, y, width, height] = box
  const paddedWidth = width + IMAGE_PADDING * 2
  const paddedHeight = height + IMAGE_PADDING * 2
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
  svg.setAttribute('viewBox', `${x - IMAGE_PADDING} ${y - IMAGE_PADDING} ${paddedWidth} ${paddedHeight}`)
  svg.setAttribute('width', String(paddedWidth))
  svg.setAttribute('height', String(paddedHeight))
  svg.style.maxWidth = 'none'
  svg.style.backgroundColor = background
  return `data:image/svg+xml,${encodeURIComponent(new XMLSerializer().serializeToString(svg))}`
}
