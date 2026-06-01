import { spawnSync } from 'child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync } from 'fs'
import os from 'os'
import path from 'path'
import process from 'process'

const [platform, arch] = process.argv.slice(2)
if (!platform || !arch) {
  console.error('usage: node scripts/install-target-optional-deps.mjs <platform> <arch>')
  process.exit(1)
}

const optionalDependencySources = ['@lydell/node-pty', '@vscode/ripgrep']
const packages = optionalDependencySources.map((source) => {
  const manifest = readManifest(source)
  const packageName = `${source}-${platform}-${arch}`
  const version = manifest.optionalDependencies?.[packageName]
  if (!version) {
    throw new Error(`${source} does not declare optional dependency ${packageName}`)
  }
  return { packageName, version }
})

const missing = packages.filter(({ packageName }) => !isInstalled(packageName))
if (missing.length === 0) {
  console.log(`[optional-deps] ${platform}-${arch} runtime packages already installed.`)
  process.exit(0)
}

const specs = missing.map(({ packageName, version }) => `${packageName}@${version}`)
console.log(`[optional-deps] Materializing ${specs.join(', ')}`)

const tempRoot = mkdtempSync(path.join(os.tmpdir(), 'dotcraft-optional-deps-'))
try {
  for (const { packageName, version } of missing) {
    materializePackage(packageName, version, tempRoot)
  }
} finally {
  rmSync(tempRoot, { recursive: true, force: true })
}

function isInstalled(packageName) {
  return existsSync(path.join(packageRoot(packageName), 'package.json'))
}

function readManifest(packageName) {
  return JSON.parse(readFileSync(path.join(packageRoot(packageName), 'package.json'), 'utf8'))
}

function packageRoot(packageName) {
  return path.join(process.cwd(), 'node_modules', ...packageName.split('/'))
}

function materializePackage(packageName, version, tempRoot) {
  const spec = `${packageName}@${version}`
  const packageTemp = mkdtempSync(path.join(tempRoot, packageName.replace(/[\/@]/g, '-') + '-'))
  run('npm', ['pack', spec, '--pack-destination', packageTemp])

  const tarballs = readdirSync(packageTemp).filter((entry) => entry.endsWith('.tgz'))
  if (tarballs.length !== 1) {
    throw new Error(`Expected one npm pack tarball for ${spec}, found ${tarballs.length}`)
  }

  const destination = packageRoot(packageName)
  mkdirSync(destination, { recursive: true })
  run('tar', [
    '-xzf',
    path.join(packageTemp, tarballs[0]),
    '-C',
    destination,
    '--strip-components=1'
  ])
}

function run(command, args) {
  let result
  if (process.platform === 'win32') {
    const shellCommand = [command, ...args].map(quoteForCmd).join(' ')
    result = spawnSync(process.env.ComSpec ?? 'cmd.exe', ['/d', '/c', shellCommand], {
      cwd: process.cwd(),
      stdio: 'inherit'
    })
  } else {
    result = spawnSync(command, args, {
      cwd: process.cwd(),
      stdio: 'inherit'
    })
  }

  if (result.error) {
    throw result.error
  }

  if (result.status !== 0) {
    process.exit(result.status ?? 1)
  }
}

function quoteForCmd(value) {
  const text = String(value)
  if (/^[A-Za-z0-9_@%+=:,./\\~-]+$/.test(text)) return text
  return `"${text.replace(/"/g, '""')}"`
}
