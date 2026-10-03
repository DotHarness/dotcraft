import { describe, expect, it } from 'vitest'
import { parsePairingParams, parsePairingUrl } from './pairing'

const FP = '3f9c1b0e5d2a4c7f8b6e1d0a9c3f5e7b2a4d6c8e0f1a3b5c7d9e2f4a6b8c0d1e'

describe('pairing code', () => {
  it('parses the Hub QR payload and the same values as deep link params', () => {
    const offer = {
      name: 'Ann’s PC',
      port: 47610,
      fingerprint: FP,
      addresses: ['192.168.1.20', '100.101.102.103'],
      code: 'q3Zt-_x',
      relay: null,
    }
    expect(
      parsePairingUrl(`dotcraft://pair?v=1&name=Ann%E2%80%99s%20PC&port=47610&fp=${FP}&addr=192.168.1.20,100.101.102.103&code=q3Zt-_x`),
    ).toEqual(offer)
    expect(parsePairingParams({ v: '1', name: 'Ann’s PC', port: '47610', fp: FP, addr: '192.168.1.20,100.101.102.103', code: ['q3Zt-_x'] })).toEqual(
      offer,
    )
  })

  it('reads the relay and host id when the computer has a relay', () => {
    const url = `dotcraft://pair?v=1&name=PC&port=47610&fp=${FP}&addr=10.0.0.2&code=abc&relay=https%3A%2F%2Frelay.example.com&host=h1`
    expect(parsePairingUrl(url)?.relay).toEqual({ url: 'https://relay.example.com', hostId: 'h1' })
    expect(parsePairingUrl(`dotcraft://pair?v=1&name=PC&port=47610&fp=${FP}&addr=10.0.0.2&code=abc&relay=https%3A%2F%2Frelay.example.com`)?.relay).toBeNull()
  })

  it('rejects other versions, schemes, and codes with a missing field', () => {
    const base = `name=PC&port=47610&fp=${FP}&addr=10.0.0.2`
    expect(parsePairingUrl(`dotcraft://pair?v=2&${base}&code=abc`)).toBeNull()
    expect(parsePairingUrl(`https://pair?v=1&${base}&code=abc`)).toBeNull()
    expect(parsePairingUrl(`dotcraft://pair?v=1&${base}`)).toBeNull()
  })
})
