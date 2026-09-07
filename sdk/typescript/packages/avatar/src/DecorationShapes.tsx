import type { ReactNode } from 'react'

export function Silhouette({ d, fill, children }: { d: string; fill: string; children?: ReactNode }) {
  return <><path d={d} fill={fill} stroke="#fff" strokeWidth="18" strokeLinejoin="round" paintOrder="stroke fill" />{children}</>
}
export function Detail({ children }: { children: ReactNode }) {
  return <g className="dca-decoration-detail">{children}</g>
}
