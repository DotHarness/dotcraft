'use strict'

if (process.platform === 'win32') {
  const fsPromises = require('node:fs/promises')
  const originalRename = fsPromises.rename.bind(fsPromises)
  const retryableCodes = new Set(['EPERM', 'EACCES'])
  const retryDelaysMs = [100, 250, 500, 1000, 2000, 3000]

  const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

  function normalizePath(value) {
    return String(value).replace(/\\/g, '/').toLowerCase()
  }

  function shouldRetry(error, oldPath, newPath) {
    if (!error || !retryableCodes.has(error.code)) {
      return false
    }

    const from = normalizePath(oldPath)
    const to = normalizePath(newPath)
    return from.endsWith('/dist/win-unpacked.tmp') && to.endsWith('/dist/win-unpacked')
  }

  fsPromises.rename = async function renameWithWindowsRetry(oldPath, newPath) {
    for (let attempt = 0; ; attempt += 1) {
      try {
        return await originalRename(oldPath, newPath)
      } catch (error) {
        if (attempt >= retryDelaysMs.length || !shouldRetry(error, oldPath, newPath)) {
          throw error
        }

        const delay = retryDelaysMs[attempt]
        console.warn(
          `[electron-builder-rename-retry] rename ${error.code}; retrying in ${delay}ms`,
        )
        await sleep(delay)
      }
    }
  }
}
