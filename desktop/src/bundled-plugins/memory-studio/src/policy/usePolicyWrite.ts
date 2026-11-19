import type { ConfigEdit } from '@dotcraft/sdk/contracts'
import { useState } from 'react'
import { errorCode, errorMessage } from '../data/rpc'
import { useT } from '../i18n'
import { memoryStudioHost } from '../runtime'
import type { PolicyLayer } from './policy'
import { writePolicyLayer } from './policyWrites'

export function usePolicyWrite(onChanged: () => void): {
  busy: boolean
  write(layer: PolicyLayer | undefined, edits: ConfigEdit[]): Promise<boolean>
} {
  const t = useT()
  const [busy, setBusy] = useState(false)
  const write = async (layer: PolicyLayer | undefined, edits: ConfigEdit[]): Promise<boolean> => {
    const host = memoryStudioHost()
    setBusy(true)
    try {
      const result = await writePolicyLayer(host.appServer, layer, edits)
      if (result?.status === 'okOverridden') host.ui.showToast({ tone: 'warning', message: t('policy.overridden') })
      onChanged()
      return true
    } catch (error) {
      const conflict = errorCode(error) === 'configVersionConflict'
        || (error as { data?: { configWriteErrorCode?: string } })?.data?.configWriteErrorCode === 'configVersionConflict'
      host.ui.showToast({ tone: 'error', message: conflict ? t('policy.layerChanged') : t('policy.saveFailed'), description: conflict ? undefined : errorMessage(error) })
      if (conflict) onChanged()
      return false
    } finally {
      setBusy(false)
    }
  }
  return { busy, write }
}
