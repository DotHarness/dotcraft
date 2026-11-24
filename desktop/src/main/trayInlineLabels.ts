import { app } from 'electron'
import { createRequire } from 'node:module'
import { join } from 'node:path'

interface TrayInlineLabelsNative {
  setCommandIds(ids: readonly number[]): boolean
}

const require = createRequire(import.meta.url)

function loadNative(): TrayInlineLabelsNative | undefined {
  if (process.platform !== 'win32' || process.arch !== 'x64') return undefined
  const root = app.isPackaged ? process.resourcesPath : join(app.getAppPath(), 'resources')
  return require(join(root, 'native', 'tray-inline.node')) as TrayInlineLabelsNative
}

export function createTrayInlineLabels() {
  let native: TrayInlineLabelsNative | null | undefined
  return (ids: readonly number[]): void => {
    if (native === null || (!native && !ids.length)) return
    try {
      native ??= loadNative()
      if (native?.setCommandIds(ids) !== true) native = null
    } catch (error) {
      native = null
      console.warn('[desktop] optional Windows tray layout unavailable', error)
    }
  }
}
