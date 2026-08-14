import { createHash } from 'node:crypto'
import { constants } from 'node:fs'
import { access, readFile } from 'node:fs/promises'
import path from 'node:path'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const packageRoot = path.resolve(scriptDir, '..')
const options = parseOptions(process.argv.slice(2))
const moduleRoot = path.resolve(packageRoot, options['module-root'] ?? '.')
const platform = options.platform ?? process.platform
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
  || catalog.version !== artifact.version
  || artifact.archiveSha256 !== lockedArtifact?.sha256
) {
  throw new Error('Packaged channel-feishu CLI metadata is inconsistent.')
}
if (Object.keys(catalog.commands ?? {}).length === 0) throw new Error('Packaged shortcut catalog is empty.')
if (sha256(await readFile(executable)) !== artifact.executableSha256) {
  throw new Error('Packaged channel-feishu CLI executable checksum is invalid.')
}
for (const args of [['--version'], ['skills', 'list'], ['skills', 'read', 'lark-doc']]) {
  const probe = spawnSync(executable, args, { encoding: 'utf8', env: probeEnvironment() })
  if (probe.status !== 0) throw new Error(`Packaged channel-feishu CLI probe failed for ${args.join(' ')}.`)
}
process.stdout.write(`Verified channel-feishu CLI ${artifact.version} for ${artifact.platform}-${artifact.arch}.\n`)

async function readJson(file) {
  return JSON.parse(await readFile(file, 'utf8'))
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex')
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
