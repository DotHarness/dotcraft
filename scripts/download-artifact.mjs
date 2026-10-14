import { createHash } from 'node:crypto'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const maxAttempts = 4
const concurrency = 8
const chunkSize = 1_048_576
const cacheDir = fileURLToPath(new URL('../cache/downloads/', import.meta.url))

export async function downloadArtifact(url, artifact) {
  const cachePath = path.join(cacheDir, artifact.sha256)
  try {
    const cached = await readFile(cachePath)
    if (sha256(cached) === artifact.sha256) {
      log(`Using verified cached archive ${artifact.file}.`)
      return cached
    }
    log('Cached archive checksum mismatch; downloading again.')
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
  }

  const controller = new AbortController()
  const deadline = setTimeout(() => controller.abort(new Error('Download exceeded 15 minutes.')), 900_000)
  let received = 0
  let total = 0
  const onProgress = (bytes) => { received += bytes }
  const progress = setInterval(() => {
    const size = `${(received / chunkSize).toFixed(1)} MiB`
    const suffix = total ? ` / ${(total / chunkSize).toFixed(1)} MiB (${Math.floor(received / total * 100)}%)` : ''
    log(`Downloaded ${size}${suffix}.`)
  }, 5_000)

  try {
    log(`Downloading ${url}.`)
    const probe = await downloadPart(url, '0-0', undefined, controller.signal, onProgress)
    let archive = probe.buffer
    if (probe.response.status === 206) {
      const range = probe.response.headers.get('content-range')?.match(/^bytes 0-0\/(\d+)$/)
      if (!range) throw new Error('Invalid archive size in range response.')
      total = Number(range[1])
      received = 0
      log(`Downloading ${(total / chunkSize).toFixed(1)} MiB with up to ${concurrency} connections.`)
      archive = await downloadRanges(probe.response.url, total, controller.signal, onProgress)
    }
    if (sha256(archive) !== artifact.sha256) throw new Error(`Checksum mismatch for ${artifact.file}.`)
    await mkdir(cacheDir, { recursive: true })
    await writeFile(cachePath, archive)
    log(`Downloaded and verified ${artifact.file} (${(archive.length / chunkSize).toFixed(1)} MiB).`)
    return archive
  } finally {
    controller.abort()
    clearTimeout(deadline)
    clearInterval(progress)
  }
}

async function downloadRanges(url, total, signal, onProgress) {
  const parts = new Array(Math.ceil(total / chunkSize))
  let nextPart = 0
  await Promise.all(Array.from({ length: Math.min(concurrency, parts.length) }, async () => {
    while (nextPart < parts.length) {
      const index = nextPart++
      const start = index * chunkSize
      const end = Math.min(start + chunkSize, total) - 1
      const part = await downloadPart(url, `${start}-${end}`, total, signal, onProgress)
      parts[index] = part.buffer
    }
  }))
  return Buffer.concat(parts)
}

async function downloadPart(url, range, total, signal, onProgress) {
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    signal.throwIfAborted()
    const controller = new AbortController()
    let idleTimer
    let received = 0
    const resetIdleTimer = () => {
      clearTimeout(idleTimer)
      idleTimer = setTimeout(() => controller.abort(new Error(`Range ${range} stopped receiving data.`)), 30_000)
    }
    try {
      resetIdleTimer()
      const response = await fetch(url, {
        signal: AbortSignal.any([signal, controller.signal]),
        headers: { Range: `bytes=${range}` }
      })
      if (!response.ok) throw new Error(`HTTP ${response.status} downloading range ${range}.`)
      if (total !== undefined && (response.status !== 206 || response.headers.get('content-range') !== `bytes ${range}/${total}`)) {
        throw new Error(`Invalid response for archive range ${range}.`)
      }
      const chunks = []
      for await (const chunk of response.body) {
        chunks.push(chunk)
        received += chunk.length
        onProgress(chunk.length)
        resetIdleTimer()
      }
      return { response, buffer: Buffer.concat(chunks) }
    } catch (error) {
      onProgress(-received)
      signal.throwIfAborted()
      const failure = controller.signal.aborted ? controller.signal.reason : error
      log(`Range ${range} attempt ${attempt}/${maxAttempts} failed: ${failure.message}`)
      if (attempt === maxAttempts) throw failure
    } finally {
      controller.abort()
      clearTimeout(idleTimer)
    }
    await new Promise((resolve) => setTimeout(resolve, attempt * 2_000))
  }
}

function sha256(buffer) {
  return createHash('sha256').update(buffer).digest('hex')
}

function log(message) {
  process.stdout.write(`[download] ${message}\n`)
}
