import type { ReactNode } from 'react'

export const materialBounds = { x: 0, y: 0, width: 1024, height: 1024 } as const

export function RigMaterialClip({ id }: { id: string }) {
  return <clipPath id={id} clipPathUnits="userSpaceOnUse">
    <rect x="243" y="408" width="538" height="426" rx="113" />
    <rect className="dca-part-arm-l-b" x="188" y="514" width="90" height="171" rx="19" />
    <rect className="dca-part-arm-r-b" x="746" y="514" width="90" height="171" rx="19" />
  </clipPath>
}

export function RigMaterial({ clipId, paintId, shadowId, surface, antenna }: {
  clipId: string; paintId: string; shadowId: string; surface?: ReactNode; antenna: boolean
}) {
  return <g filter={`url(#${shadowId})`}>
    <g className="dca-part-material" clipPath={`url(#${clipId})`}>
      <rect {...materialBounds} fill={`url(#${paintId})`} />
      {surface && <g className="dca-part-surface">{surface}</g>}
    </g>
    {antenna && <rect x="479" y="337" width="66" height="119" rx="6" fill={`url(#${paintId})`} />}
  </g>
}
