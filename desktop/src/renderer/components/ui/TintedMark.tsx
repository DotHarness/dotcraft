import type { CSSProperties, JSX } from 'react'

interface TintedMarkProps {
  src: string
  size: number
}

/** Paints a monochrome SVG in the current text colour; an `<img>` cannot inherit `currentColor`. */
export function TintedMark({ src, size }: TintedMarkProps): JSX.Element {
  const mask = `url("${src}") center / contain no-repeat`
  const style: CSSProperties = { width: size, height: size, display: 'block', backgroundColor: 'currentColor', WebkitMask: mask, mask }
  return <span aria-hidden="true" style={style} />
}
