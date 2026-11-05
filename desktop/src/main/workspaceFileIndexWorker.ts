export const FILE_INDEX_WORKER_SOURCE = String.raw`
const { parentPort, workerData } = require('worker_threads')
const { spawn } = require('child_process')
const path = require('path')
const fs = require('fs/promises')

const root = path.resolve(workerData.workspaceRoot)
const cachePath = workerData.cachePath
const rgPath = workerData.rgPath
const rgGlobs = workerData.rgGlobs || []
const excludedPrefixes = workerData.excludedPrefixes || []
const excludedExact = workerData.excludedExact || []
const maxEntries = workerData.maxEntries || 200000
const progressBatch = workerData.progressBatch || 5000
const quietFallbackMs = workerData.quietFallbackMs || 5000

function isExcluded(rel) {
  if (excludedExact.indexOf(rel) !== -1) return true
  for (let i = 0; i < excludedPrefixes.length; i++) {
    if (rel.indexOf(excludedPrefixes[i]) === 0) return true
  }
  return false
}

function makeEntry(relRaw) {
  const rel = relRaw.replace(/\\/g, '/')
  if (isExcluded(rel)) return null
  const base = path.basename(rel)
  if (base.endsWith('.pyc') || base.endsWith('.min.js')) return null
  const dirRaw = path.dirname(rel)
  const dir = dirRaw === '.' ? '' : dirRaw.replace(/\\/g, '/')
  return { relativePath: rel, name: base, dir: dir }
}

function reportProgress(count, source) {
  try {
    parentPort.postMessage({ type: 'progress', count: count, source: source })
  } catch (_) {}
}

async function writeCache(entries, truncated, source) {
  const cache = {
    schemaVersion: workerData.schemaVersion,
    workspaceRoot: root,
    generatedAt: new Date().toISOString(),
    ignoreConfigVersion: workerData.ignoreConfigVersion,
    truncated: truncated,
    source: source,
    entries: entries
  }
  await fs.mkdir(path.dirname(cachePath), { recursive: true })
  const tmp = cachePath + '.tmp'
  await fs.writeFile(tmp, JSON.stringify(cache), 'utf8')
  await fs.rename(tmp, cachePath)
}

function runWithRipgrep() {
  return new Promise((resolve, reject) => {
    if (!rgPath) {
      reject(new Error('ripgrep binary path not configured'))
      return
    }
    // --no-require-git makes ripgrep honour .gitignore / .ignore even when
    // the workspace is not a git repo (matches ignore-walk's behaviour).
    const args = ['--files', '--hidden', '--no-messages', '--null', '--no-require-git']
    for (let i = 0; i < rgGlobs.length; i++) {
      args.push('--glob')
      args.push('!' + rgGlobs[i])
    }
    let child
    try {
      child = spawn(rgPath, args, { cwd: root, windowsHide: true })
    } catch (err) {
      reject(err)
      return
    }

    const entries = []
    let buffer = ''
    let truncated = false
    let sawAnyOutput = false
    let settled = false

    const quietTimer = setTimeout(() => {
      if (!sawAnyOutput && !settled) {
        settled = true
        try { child.kill() } catch (_) { /* ignore */ }
        reject(new Error('ripgrep produced no output within ' + quietFallbackMs + 'ms'))
      }
    }, quietFallbackMs)

    child.stdout.setEncoding('utf8')
    child.stdout.on('data', (chunk) => {
      sawAnyOutput = true
      buffer += chunk
      let nullIdx = buffer.indexOf('\0')
      while (nullIdx !== -1) {
        const piece = buffer.slice(0, nullIdx)
        buffer = buffer.slice(nullIdx + 1)
        if (piece) {
          if (entries.length >= maxEntries) {
            truncated = true
            try { child.kill() } catch (_) { /* ignore */ }
            break
          }
          const entry = makeEntry(piece)
          if (entry) {
            entries.push(entry)
            if (entries.length % progressBatch === 0) {
              reportProgress(entries.length, 'rg')
            }
          }
        }
        nullIdx = buffer.indexOf('\0')
      }
    })

    child.on('error', (err) => {
      if (settled) return
      settled = true
      clearTimeout(quietTimer)
      reject(err)
    })
    child.on('close', (code) => {
      if (settled) return
      settled = true
      clearTimeout(quietTimer)
      if (truncated) {
        resolve({ entries: entries, truncated: true, source: 'rg' })
        return
      }
      if (code === 0 || code === null) {
        const tail = buffer.trim()
        if (tail) {
          const entry = makeEntry(tail)
          if (entry && entries.length < maxEntries) entries.push(entry)
        }
        resolve({ entries: entries, truncated: false, source: 'rg' })
      } else {
        reject(new Error('ripgrep exited with code ' + code))
      }
    })
  })
}

async function runWithIgnoreWalk() {
  const walk = require('ignore-walk')
  const paths = await walk({
    path: root,
    ignoreFiles: ['.gitignore', '.ignore'],
    includeEmpty: false
  })
  const entries = []
  let truncated = false
  for (let i = 0; i < paths.length; i++) {
    const entry = makeEntry(paths[i])
    if (!entry) continue
    if (entries.length >= maxEntries) {
      truncated = true
      break
    }
    entries.push(entry)
    if (entries.length % progressBatch === 0) {
      reportProgress(entries.length, 'walk')
    }
  }
  return { entries: entries, truncated: truncated, source: 'walk' }
}

;(async () => {
  let result
  try {
    result = await runWithRipgrep()
  } catch (rgErr) {
    reportProgress(0, 'walk')
    try {
      result = await runWithIgnoreWalk()
      result.rgError = rgErr && rgErr.message ? rgErr.message : String(rgErr)
    } catch (walkErr) {
      parentPort.postMessage({
        type: 'error',
        error: 'ripgrep failed (' + (rgErr && rgErr.message ? rgErr.message : String(rgErr)) +
               ') and ignore-walk failed (' + (walkErr && walkErr.message ? walkErr.message : String(walkErr)) + ')'
      })
      return
    }
  }
  try {
    await writeCache(result.entries, !!result.truncated, result.source)
  } catch (writeErr) {
    parentPort.postMessage({
      type: 'error',
      error: 'failed to write cache: ' + (writeErr && writeErr.message ? writeErr.message : String(writeErr))
    })
    return
  }
  parentPort.postMessage({
    type: 'success',
    entries: result.entries,
    pathCount: result.entries.length,
    truncated: !!result.truncated,
    source: result.source,
    rgError: result.rgError || null
  })
})().catch((error) => {
  parentPort.postMessage({
    type: 'error',
    error: error && error.message ? error.message : String(error)
  })
})
`
