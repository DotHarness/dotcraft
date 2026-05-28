import { existsSync } from 'node:fs'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)

try {
  const electronPath = require('electron')

  if (typeof electronPath !== 'string' || !existsSync(electronPath)) {
    throw new Error(`Electron executable was not found at ${electronPath}`)
  }

  console.log(`[ensure-electron] ${electronPath}`)
} catch (error) {
  console.error('[ensure-electron] Electron binary is unavailable.')
  console.error(error instanceof Error ? error.message : String(error))
  process.exit(1)
}
