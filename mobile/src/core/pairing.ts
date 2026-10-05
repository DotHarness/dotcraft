import type { RelayInfo } from './gateway'

export interface PairingOffer {
  id: string
  name: string
  port: number
  fingerprint: string
  addresses: string[]
  code: string
  relay: RelayInfo | null
}

export function parsePairingParams(params: Record<string, string | string[] | undefined>): PairingOffer | null {
  const read = (key: string): string => {
    const value = params[key]
    return (Array.isArray(value) ? value[0] : value) ?? ''
  }
  const [id, name, code, fingerprint, addr] = [read('id'), read('name'), read('code'), read('fp'), read('addr')]
  const [port, relay] = [Number(read('port')), read('relay')]
  if (read('v') !== '1' || !id || !name || !code || !fingerprint || !addr || !port) return null
  return { id, name, port, fingerprint, addresses: addr.split(','), code, relay: relay ? { url: relay } : null }
}

export function parsePairingUrl(url: string): PairingOffer | null {
  const match = /^dotcraft:\/\/pair\?(.*)$/.exec(url.trim())
  if (!match) return null
  const params: Record<string, string> = {}
  for (const pair of match[1].split('&')) {
    const index = pair.indexOf('=')
    if (index > 0) params[pair.slice(0, index)] = decodeURIComponent(pair.slice(index + 1))
  }
  return parsePairingParams(params)
}
