import { createServer, type Server } from 'node:net'

import { checkDebugPort } from './check-debug-port.mjs'

describe('checkDebugPort', () => {
  let occupied: Server | undefined

  afterEach(async () => {
    if (!occupied) return
    await new Promise<void>((resolve, reject) => {
      occupied?.close((error) => error ? reject(error) : resolve())
    })
    occupied = undefined
  })

  it('accepts an available loopback port', async () => {
    const port = await reserveThenReleasePort()

    await expect(checkDebugPort(port)).resolves.toBeUndefined()
  })

  it('reports the endpoint and rejects an occupied port', async () => {
    occupied = createServer()
    await listen(occupied)
    const address = occupied.address()
    if (!address || typeof address === 'string') throw new Error('Expected a TCP address.')

    await expect(checkDebugPort(address.port))
      .rejects.toThrow(`http://127.0.0.1:${address.port}`)
  })
})

async function reserveThenReleasePort(): Promise<number> {
  const server = createServer()
  await listen(server)
  const address = server.address()
  if (!address || typeof address === 'string') throw new Error('Expected a TCP address.')
  await new Promise<void>((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve())
  })
  return address.port
}

function listen(server: Server): Promise<void> {
  return new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen({ host: '127.0.0.1', port: 0, exclusive: true }, resolve)
  })
}
