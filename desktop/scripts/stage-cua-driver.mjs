import { createHash } from 'node:crypto'
import { spawnSync } from 'node:child_process'
import { copyFile, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const lock = JSON.parse(await readFile(path.join(desktopRoot, 'src', 'main', 'computerUse', 'cua-driver.lock.json'), 'utf8'))
const options = parseOptions(process.argv.slice(2))
const platform = options.platform ?? process.platform
const arch = options.arch ?? process.arch

if (platform !== 'win32') {
  process.stdout.write(`[cua-driver] Computer use is Windows-only; nothing to stage for ${platform}-${arch}.\n`)
  process.exit(0)
}

const artifact = lock.artifacts[`${platform}-${arch}`]
if (!artifact) throw new Error(`No pinned cua-driver artifact for ${platform}-${arch}.`)

const outputDir = path.join(desktopRoot, 'resources', 'bin', 'cua-driver')
const executable = path.join(outputDir, 'cua-driver.exe')
const markerPath = path.join(outputDir, 'cua-driver-artifact.json')

if (await isCurrent()) {
  process.stdout.write(`[cua-driver] ${lock.version} for ${platform}-${arch} is already staged.\n`)
  process.exit(0)
}

const temporaryRoot = await mkdtemp(path.join(tmpdir(), 'dotcraft-cua-driver-'))
try {
  const url = `https://github.com/${lock.repository}/releases/download/${lock.tag}/${artifact.file}`
  const archive = await downloadWithRetry(url)
  if (sha256(archive) !== artifact.sha256) throw new Error(`Checksum mismatch for ${artifact.file}.`)
  const archivePath = path.join(temporaryRoot, artifact.file)
  await writeFile(archivePath, archive)

  const extractedDir = path.join(temporaryRoot, 'extracted')
  await mkdir(extractedDir)
  const tar = process.platform === 'win32' ? path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'tar.exe') : 'tar'
  const extraction = spawnSync(tar, ['-xf', archivePath, '-C', extractedDir], { encoding: 'utf8' })
  if (extraction.status !== 0) throw new Error(`Failed to extract ${artifact.file}: ${extraction.stderr}`)
  const stagedExecutable = await findFile(extractedDir, 'cua-driver.exe')
  if (!stagedExecutable) throw new Error(`${artifact.file} does not contain cua-driver.exe.`)
  if (process.platform === 'win32') verifySigner(stagedExecutable)

  await rm(outputDir, { recursive: true, force: true })
  await mkdir(outputDir, { recursive: true })
  await copyFile(stagedExecutable, executable)
  await copyFile(path.join(desktopRoot, 'resources', 'cua-driver', 'LICENSE'), path.join(outputDir, 'LICENSE'))
  await writeFile(markerPath, `${JSON.stringify({
    version: lock.version,
    platform,
    arch,
    archiveSha256: artifact.sha256,
    executableSha256: sha256(await readFile(executable))
  }, null, 2)}\n`)
  process.stdout.write(`[cua-driver] Staged ${lock.version} for ${platform}-${arch}.\n`)
} finally {
  await rm(temporaryRoot, { recursive: true, force: true })
}

async function isCurrent() {
  if (!existsSync(executable) || !existsSync(markerPath)) return false
  try {
    const marker = JSON.parse(await readFile(markerPath, 'utf8'))
    return marker.version === lock.version
      && marker.arch === arch
      && marker.archiveSha256 === artifact.sha256
      && marker.executableSha256 === sha256(await readFile(executable))
  } catch {
    return false
  }
}

function verifySigner(file) {
  const script = `$s = Get-AuthenticodeSignature -LiteralPath '${file.replace(/'/g, "''")}'; "$($s.Status)|$($s.SignerCertificate.Subject)"`
  // A PSModulePath inherited from PowerShell 7 stops Windows PowerShell from loading Get-AuthenticodeSignature.
  const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => key.toLowerCase() !== 'psmodulepath'))
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8', env })
  const [status, subject = ''] = (result.stdout ?? '').trim().split('|')
  if (status !== 'Valid' || !subject.includes(`CN="${lock.signer}"`)) {
    throw new Error(`cua-driver.exe signature is not valid for ${lock.signer} (status ${status || 'unknown'}). ${result.stderr ?? ''}`.trim())
  }
}

async function findFile(root, name) {
  for (const entry of await readdir(root, { withFileTypes: true })) {
    const full = path.join(root, entry.name)
    if (entry.isFile() && entry.name.toLowerCase() === name) return full
    if (entry.isDirectory()) {
      const nested = await findFile(full, name)
      if (nested) return nested
    }
  }
  return null
}

async function downloadWithRetry(url) {
  let lastError
  for (let attempt = 1; attempt <= 4; attempt++) {
    try {
      const response = await fetch(url)
      if (!response.ok) throw new Error(`HTTP ${response.status} for ${url}`)
      return Buffer.from(await response.arrayBuffer())
    } catch (error) {
      lastError = error
      await new Promise((resolve) => setTimeout(resolve, attempt * 2000))
    }
  }
  throw lastError
}

function sha256(buffer) {
  return createHash('sha256').update(buffer).digest('hex')
}

function parseOptions(args) {
  const parsed = {}
  for (let index = 0; index < args.length; index += 2) {
    const key = args[index]
    if (!key?.startsWith('--') || args[index + 1] === undefined) throw new Error(`Invalid argument: ${key}`)
    parsed[key.slice(2)] = args[index + 1]
  }
  return parsed
}
