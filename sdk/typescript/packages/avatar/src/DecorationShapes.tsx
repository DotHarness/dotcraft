import { useId, type ReactNode } from 'react'
import type { MascotExpression } from './MascotRig.js'

export const AVATAR_RIG_GEOMETRY = {
  viewBoxSize: 1024,
  body: { x: 243, y: 408, width: 538, height: 426, rx: 113, centerX: 512, centerY: 621 },
  screen: { x: 307, y: 476, width: 410, height: 291, rx: 68 },
  topContactY: 400,
  browY: 436,
  chinY: 824,
  temple: { x: 800, y: 410 },
  leftHand: { x: 233, y: 645 },
} as const

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

export function FaceplateSurface({ id, fill, children }: { id?: string; fill: string; children?: ReactNode }) {
  const clip = useClipId()
  return <g data-faceplate={id}>
    <defs><clipPath id={clip}><rect x="307" y="476" width="410" height="291" rx="68" /></clipPath></defs>
    <rect x="307" y="476" width="410" height="291" rx="68" fill={fill} />
    {children && <g clipPath={`url(#${clip})`}>{children}</g>}
  </g>
}

export function FaceplateLayer({ expression, children, extra }: { expression: MascotExpression; children: ReactNode; extra?: ReactNode }) {
  return <g className={`dca-part-face dca-part-face-${expression}`} data-mask-face={expression}>
    <g className="dca-part-eyes">{children}</g>
    {extra}
  </g>
}
