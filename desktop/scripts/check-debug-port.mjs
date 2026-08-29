import { createServer } from 'node:net'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export function checkDebugPort(port) {
  if (!Number.isInteger(port) || port < 1 || port > 65_535) {
    return Promise.reject(new Error(`Invalid CDP port: ${port}`))
  }

  const endpoint = `http://127.0.0.1:${port}`
  return new Promise((resolveCheck, rejectCheck) => {
    const server = createServer()
    server.once('error', (error) => {
      const detail = error?.code === 'EADDRINUSE' ? 'is already in use' : `is unavailable: ${error.message}`
      rejectCheck(new Error(`DotCraft CDP endpoint ${detail}: ${endpoint}`, { cause: error }))
    })
    server.listen({ host: '127.0.0.1', port, exclusive: true }, () => {
      server.close((error) => {
        if (error) rejectCheck(error)
        else resolveCheck()
      })
    })
  })
}

const isMain = process.argv[1]
  && resolve(process.argv[1]) === fileURLToPath(import.meta.url)

if (isMain) {
  const port = Number(process.argv[2])
  try {
    await checkDebugPort(port)
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error))
    process.exitCode = 1
  }
}
