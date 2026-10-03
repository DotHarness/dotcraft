import { useMemo, type JSX } from 'react'
import { encode } from 'uqr'

import { useT } from '../../../../contexts/LocaleContext'

const QUIET_ZONE = 4

function modulePath(modules: boolean[][]): string {
  let path = ''
  modules.forEach((row, y) => {
    let x = 0
    while (x < row.length) {
      if (!row[x]) {
        x += 1
        continue
      }
      const start = x
      while (x < row.length && row[x]) x += 1
      path += `M${start} ${y}h${x - start}v1h-${x - start}z`
    }
  })
  return path
}

export function PairingQrCode({ payload, expired }: { payload: string; expired: boolean }): JSX.Element {
  const t = useT()
  const qr = useMemo(() => encode(payload, { ecc: 'M', border: QUIET_ZONE }), [payload])
  const path = useMemo(() => modulePath(qr.data), [qr])

  return (
    <div
      className="dc-add-phone__qr"
      role="img"
      aria-label={expired ? t('settings.phones.pair.qrExpiredLabel') : t('settings.phones.pair.qrLabel')}
      data-expired={expired ? 'true' : undefined}
    >
      <svg viewBox={`0 0 ${qr.size} ${qr.size}`} width="184" height="184" shapeRendering="crispEdges" aria-hidden="true">
        <path d={path} />
      </svg>
      {expired && <span className="dc-add-phone__qr-veil">{t('settings.phones.pair.expiredVeil')}</span>}
    </div>
  )
}
