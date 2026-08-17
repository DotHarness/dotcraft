import { createHash } from 'node:crypto'
import { execFile } from 'node:child_process'
import { constants } from 'node:fs'
import { access, readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'

const execFileAsync = promisify(execFile)

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const packageRoot = path.resolve(scriptDir, '..')
const options = parseOptions(process.argv.slice(2))
const moduleRoot = path.resolve(packageRoot, options['module-root'] ?? '.')
const platform = options.platform ?? process.platform
const arch = options.arch ?? process.arch
const vendorRoot = path.join(moduleRoot, 'vendor')
const executable = path.join(vendorRoot, platform === 'win32' ? 'lark-cli.exe' : 'lark-cli')
const [lock, artifact, catalog] = await Promise.all([
  readJson(path.join(packageRoot, 'official-cli', 'lark-cli.lock.json')),
  readJson(path.join(vendorRoot, 'lark-cli-artifact.json')),
  readJson(path.join(vendorRoot, 'lark-cli-shortcuts.json')),
  access(path.join(vendorRoot, 'LICENSE'), constants.R_OK)
])
await access(executable, platform === 'win32' ? constants.R_OK : constants.R_OK | constants.X_OK)
const lockedArtifact = lock.artifacts?.[`${artifact.platform}-${artifact.arch}`]
if (
  artifact.version !== lock.version
  || artifact.platform !== platform
  || artifact.arch !== arch
  || catalog.version !== artifact.version
  || artifact.archiveSha256 !== lockedArtifact?.sha256
) {
  throw new Error('Packaged channel-feishu CLI metadata is inconsistent.')
}
if (Object.keys(catalog.commands ?? {}).length === 0) throw new Error('Packaged shortcut catalog is empty.')
if (sha256(await readFile(executable)) !== artifact.executableSha256) {
  throw new Error('Packaged channel-feishu CLI executable checksum is invalid.')
}
await verifyBotCredentialContract(executable)
process.stdout.write(`Verified channel-feishu CLI ${artifact.version} for ${artifact.platform}-${artifact.arch}.\n`)

async function verifyBotCredentialContract(executablePath) {
  const baseEnv = Object.fromEntries(
    Object.entries(process.env).filter(([key]) => !key.toUpperCase().startsWith('LARKSUITE_CLI_'))
  )
  Object.assign(baseEnv, {
    LARKSUITE_CLI_APP_ID: 'cli_dotcraft_verification',
    LARKSUITE_CLI_BRAND: 'feishu',
    LARKSUITE_CLI_DEFAULT_AS: 'bot',
    LARKSUITE_CLI_STRICT_MODE: 'bot',
    LARKSUITE_CLI_NO_UPDATE_NOTIFIER: '1'
  })
  const args = [
    'docs', '+fetch', '--doc', 'doccn_DOTCRAFT_VERIFY',
    '--doc-format', 'markdown', '--dry-run', '--as', 'bot'
  ]

  const success = await runCli(executablePath, args, {
    ...baseEnv,
    LARKSUITE_CLI_TENANT_ACCESS_TOKEN: 't-dotcraft-verification'
  })
  const successEnvelope = parseEnvelope(success.stdout)
  if (success.exitCode !== 0 || successEnvelope?.ok !== true
      || successEnvelope?.identity !== 'bot' || successEnvelope?.dry_run !== true) {
    throw new Error('Packaged channel-feishu CLI failed the Bot tenant-token dry-run probe.')
  }

  const userIdentity = await runCli(
    executablePath,
    args.map(arg => arg === 'bot' ? 'user' : arg),
    { ...baseEnv, LARKSUITE_CLI_TENANT_ACCESS_TOKEN: 't-dotcraft-verification' }
  )
  const userEnvelope = parseEnvelope(userIdentity.stdout) ?? parseEnvelope(userIdentity.stderr)
  if (userIdentity.exitCode !== 2
      || userEnvelope?.identity !== 'user'
      || userEnvelope?.error?.type !== 'validation') {
    throw new Error('Packaged channel-feishu CLI failed the forced Bot identity probe.')
  }

  const missing = await runCli(executablePath, args.filter(arg => arg !== '--dry-run'), {
    ...baseEnv,
    LARKSUITE_CLI_APP_SECRET: 'verification-only-secret'
  })
  const missingEnvelope = parseEnvelope(missing.stdout) ?? parseEnvelope(missing.stderr)
  if (missing.exitCode !== 3
      || missingEnvelope?.error?.type !== 'authentication'
      || missingEnvelope?.error?.subtype !== 'token_missing') {
    throw new Error('Packaged channel-feishu CLI no longer reports the expected missing-token contract.')
  }
}

async function runCli(executablePath, args, env) {
  try {
    const result = await execFileAsync(executablePath, args, {
      env,
      windowsHide: true,
      timeout: 15000,
      maxBuffer: 1024 * 1024
    })
    return { exitCode: 0, stdout: result.stdout, stderr: result.stderr }
  } catch (error) {
    return {
      exitCode: typeof error.code === 'number' ? error.code : -1,
      stdout: typeof error.stdout === 'string' ? error.stdout : '',
      stderr: typeof error.stderr === 'string' ? error.stderr : ''
    }
  }
}

function parseEnvelope(value) {
  try {
    return JSON.parse(value.trim())
  } catch {
    return undefined
  }
}

async function readJson(file) {
  return JSON.parse(await readFile(file, 'utf8'))
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
