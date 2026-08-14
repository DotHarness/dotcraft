import { createHash } from 'node:crypto'
import { chmod, copyFile, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const packageRoot = path.resolve(scriptDir, '..')
const lock = JSON.parse(await readFile(path.join(packageRoot, 'official-cli', 'lark-cli.lock.json'), 'utf8'))
const options = parseOptions(process.argv.slice(2))
const platform = options.platform ?? process.platform
const arch = options.arch ?? process.arch
const target = lock.artifacts[`${platform}-${arch}`]
if (!target) throw new Error(`No pinned lark-cli artifact for ${platform}-${arch}.`)

const outputDir = options.output ? path.resolve(options.output) : path.join(packageRoot, 'vendor')
const temporaryRoot = await mkdtemp(path.join(tmpdir(), 'dotcraft-lark-cli-'))
try {
  const archivePath = path.join(temporaryRoot, target.file)
  const response = await fetch(
    `https://github.com/${lock.repository}/releases/download/v${lock.version}/${target.file}`,
    { redirect: 'follow' }
  )
  if (!response.ok) throw new Error(`Failed to download pinned lark-cli artifact: HTTP ${response.status}.`)
  const archive = Buffer.from(await response.arrayBuffer())
  const archiveSha256 = sha256(archive)
  if (archiveSha256 !== target.sha256) throw new Error(`Checksum mismatch for lark-cli ${platform}-${arch}.`)
  await writeFile(archivePath, archive)

  const extractedDir = path.join(temporaryRoot, 'extracted')
  await mkdir(extractedDir)
  const extraction = spawnSync('tar', ['-xf', archivePath, '-C', extractedDir], { encoding: 'utf8' })
  if (extraction.status !== 0) throw new Error(`Failed to extract lark-cli ${platform}-${arch}.`)

  const executableName = platform === 'win32' ? 'lark-cli.exe' : 'lark-cli'
  const destinationExecutable = path.join(outputDir, executableName)
  await mkdir(outputDir, { recursive: true })
  await copyFile(path.join(extractedDir, executableName), destinationExecutable)
  if (platform !== 'win32') await chmod(destinationExecutable, 0o755)
  await copyFile(path.join(extractedDir, 'LICENSE'), path.join(outputDir, 'LICENSE'))

  const version = run(destinationExecutable, ['--version'])
  if (!version.includes(`version ${lock.version}`)) throw new Error('Staged lark-cli version does not match the lock.')
  const commands = generateShortcutCatalog(destinationExecutable)
  await writeFile(
    path.join(outputDir, 'lark-cli-shortcuts.json'),
    `${JSON.stringify({ version: lock.version, commands }, null, 2)}\n`
  )
  await writeFile(
    path.join(outputDir, 'lark-cli-artifact.json'),
    `${JSON.stringify({
      version: lock.version,
      platform,
      arch,
      archiveSha256,
      executableSha256: sha256(await readFile(destinationExecutable))
    }, null, 2)}\n`
  )
  process.stdout.write(`Staged channel-feishu lark-cli ${lock.version} for ${platform}-${arch}.\n`)
} finally {
  await rm(temporaryRoot, { recursive: true, force: true })
}

function generateShortcutCatalog(executable) {
  const domains = [
    'approval', 'attendance', 'base', 'calendar', 'contact', 'docs', 'drive', 'im',
    'mail', 'markdown', 'mindnotes', 'minutes', 'note', 'okr', 'sheets', 'slides',
    'task', 'vc', 'whiteboard', 'wiki'
  ]
  const commands = {}
  for (const domain of domains) {
    const shortcuts = [...run(executable, [domain, '--help']).matchAll(/^\s{2}(\+[^\s]+)\s+/gm)]
      .map((match) => match[1])
    for (const shortcut of shortcuts) {
      const risk = run(executable, [domain, shortcut, '--help'])
        .match(/^Risk:\s+(read|write|high-risk-write)\s*$/m)?.[1]
      if (!risk) throw new Error(`Shortcut ${domain} ${shortcut} has no recognized risk.`)
      commands[`${domain} ${shortcut}`] = risk
    }
  }
  return Object.fromEntries(Object.entries(commands).sort(([left], [right]) => left.localeCompare(right)))
}

function run(executable, args) {
  const result = spawnSync(executable, args, { encoding: 'utf8', env: probeEnvironment() })
  if (result.status !== 0) throw new Error(`Pinned lark-cli probe failed for ${args.join(' ')}.`)
  return result.stdout
}

function probeEnvironment() {
  return {
    ...process.env,
    LARKSUITE_CLI_USER_ACCESS_TOKEN: '',
    LARKSUITE_CLI_TENANT_ACCESS_TOKEN: '',
    LARKSUITE_CLI_DEFAULT_AS: 'bot',
    LARKSUITE_CLI_STRICT_MODE: 'bot',
    LARKSUITE_CLI_NO_UPDATE_NOTIFIER: '1'
  }
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex')
}

function parseOptions(args) {
  const result = {}
  for (let index = 0; index < args.length; index += 2) {
    const key = args[index]
    const value = args[index + 1]
    if (!key?.startsWith('--') || value == null) throw new Error(`Invalid option near ${key ?? '<end>'}.`)
    result[key.slice(2)] = value
  }
  return result
}
