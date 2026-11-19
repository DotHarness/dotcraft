import type { ConfigEdit, ConfigWriteResult } from '@dotcraft/sdk/contracts'
import type { Rpc } from '../data/rpc'
import type { PolicyLayer } from './policy'

export async function writePolicyLayer(rpc: Rpc, layer: PolicyLayer | undefined, edits: ConfigEdit[]): Promise<ConfigWriteResult | null> {
  if (edits.length === 0) return null
  return rpc.request('config/batchWrite', {
    edits,
    ...(layer ? { filePath: layer.file, expectedVersion: layer.version } : {})
  }, 20_000)
}
