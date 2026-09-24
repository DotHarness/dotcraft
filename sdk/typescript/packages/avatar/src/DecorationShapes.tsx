import { useId, type ReactNode } from 'react'

export function Silhouette({ d, fill, stroke = 18, children }: { d: string; fill: string; stroke?: number; children?: ReactNode }) {
  return <><path d={d} fill={fill} stroke="#fff" strokeWidth={stroke} strokeLinejoin="round" paintOrder="stroke fill" />{children}</>
}
// Every contour is laid before every fill, so overlapping parts read as one sticker.
export function Plane({ d, fill, contour = 20 }: { d: string[]; fill: string; contour?: number }) {
  return <>{d.map((p, i) => <path key={`o${i}`} d={p} stroke="#fff" strokeWidth={contour} strokeLinejoin="round" />)}{d.map((p, i) => <path key={i} d={p} fill={fill} />)}</>
}
export function Detail({ children }: { children: ReactNode }) {
  return <g className="dca-decoration-detail">{children}</g>
}
export function Glow({ children, blur = 18, className = '' }: { children: ReactNode; blur?: number; className?: string }) {
  const id = `dca-fx-blur-${useId().replace(/:/g, '')}`
  return <>
    <defs><filter id={id} x="-40%" y="-40%" width="180%" height="180%"><feGaussianBlur stdDeviation={blur} /></filter></defs>
    <g className={`dca-fx dca-fx-glow ${className}`.trim()} filter={`url(#${id})`}>{children}</g>
  </>
}
export function useClipId() { return `dca-clip-${useId().replace(/:/g, '')}` }
