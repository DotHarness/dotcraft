import { describe, expect, it } from 'vitest'
import { parsePairingParams, parsePairingUrl } from './pairing'

const FP = '3f9c1b0e5d2a4c7f8b6e1d0a9c3f5e7b2a4d6c8e0f1a3b5c7d9e2f4a6b8c0d1e'

describe('pairing code', () => {
  it('parses the Hub QR payload and the same values as deep link params', () => {
    const offer = {
      id: 'W2Hk9qD1xP7vZb3nLc0aTg',
      name: 'Ann’s PC',
      port: 47610,
      fingerprint: FP,
      addresses: ['192.168.1.20', '100.101.102.103'],
      code: 'q3Zt-_x',
      relay: null,
    }
    expect(
      parsePairingUrl(`dotcraft://pair?v=1&id=W2Hk9qD1xP7vZb3nLc0aTg&name=Ann%E2%80%99s%20PC&port=47610&fp=${FP}&addr=192.168.1.20,100.101.102.103&code=q3Zt-_x`),
    ).toEqual(offer)
    expect(parsePairingParams({ v: '1', id: 'W2Hk9qD1xP7vZb3nLc0aTg', name: 'Ann’s PC', port: '47610', fp: FP, addr: '192.168.1.20,100.101.102.103', code: ['q3Zt-_x'] })).toEqual(
      offer,
    )
  })

  it('reads the relay when the computer has one', () => {
    const base = `dotcraft://pair?v=1&id=pc&name=PC&port=47610&fp=${FP}&addr=10.0.0.2&code=abc`
    expect(parsePairingUrl(`${base}&relay=https%3A%2F%2Frelay.example.com`)?.relay).toEqual({ url: 'https://relay.example.com' })
    expect(parsePairingUrl(base)?.relay).toBeNull()
  })

  it('rejects other versions, schemes, and codes with a missing field', () => {
    const base = `id=pc&name=PC&port=47610&fp=${FP}&addr=10.0.0.2`
    expect(parsePairingUrl(`dotcraft://pair?v=2&${base}&code=abc`)).toBeNull()
    expect(parsePairingUrl(`https://pair?v=1&${base}&code=abc`)).toBeNull()
    expect(parsePairingUrl(`dotcraft://pair?v=1&${base}`)).toBeNull()
    expect(parsePairingUrl(`dotcraft://pair?v=1&${base.replace('id=pc&', '')}&code=abc`)).toBeNull()
  })
})
