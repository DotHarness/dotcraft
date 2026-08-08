import { existsSync, readdirSync, statSync } from 'fs'
import path from 'path'
import { extractFile, listPackage } from '@electron/asar'

const cwd = process.cwd()
const distDir = process.env.DOTCRAFT_DIST_DIR
  ? path.resolve(cwd, process.env.DOTCRAFT_DIST_DIR)
  : path.join(cwd, 'dist')

function normalizeAsarPath(value) {
  return value.replace(/\\/g, '/').replace(/^\/+/, '')
}

function existingResourcesDirs() {
  const candidates = [
    { resourcesDir: path.join(distDir, 'win-unpacked', 'resources'), platform: 'win32', arch: 'x64' },
    { resourcesDir: path.join(distDir, 'win-arm64-unpacked', 'resources'), platform: 'win32', arch: 'arm64' },
    { resourcesDir: path.join(distDir, 'win-ia32-unpacked', 'resources'), platform: 'win32', arch: 'ia32' },
    { resourcesDir: path.join(distDir, 'linux-unpacked', 'resources'), platform: 'linux', arch: undefined }
  ]

  if (existsSync(distDir)) {
    for (const name of readdirSync(distDir)) {
      const maybeMacDir = path.join(distDir, name)
      if (!statSync(maybeMacDir).isDirectory()) continue
      const appDir = path.join(maybeMacDir, 'DotCraft.app', 'Contents', 'Resources')
      if (existsSync(appDir)) {
        const arch = name.includes('arm64') ? 'arm64' : 'x64'
        candidates.push({ resourcesDir: appDir, platform: 'darwin', arch })
      }
    }
  }

  return candidates.filter((candidate) => existsSync(path.join(candidate.resourcesDir, 'app.asar')))
}

function fail(message) {
  console.error(`[verify-package] ${message}`)
  process.exitCode = 1
}

const resourcesDirs = existingResourcesDirs()
if (resourcesDirs.length === 0) {
  fail('No packaged Electron resources directory found under dist/. Run electron-builder first.')
} else {
  for (const target of resourcesDirs) {
    verifyResourcesDir(target)
  }
}

function verifyResourcesDir(target) {
  const { resourcesDir, platform, arch } = target
  const appAsar = path.join(resourcesDir, 'app.asar')
  const unpackedRoot = path.join(resourcesDir, 'app.asar.unpacked')
  const entries = new Set(listPackage(appAsar).map(normalizeAsarPath))
  const rgPath = resolveRipgrepPath(unpackedRoot, platform, arch)

  if (!rgPath) {
    const targetLabel = arch ? `${platform}-${arch}` : platform
    fail(`Missing unpacked @vscode/ripgrep binary for ${targetLabel} under app.asar.unpacked.`)
  }

  if (platform === 'win32' && arch) {
    const nativePtyFiles = [
      path.join(unpackedRoot, 'node_modules', '@lydell', `node-pty-win32-${arch}`, 'prebuilds', `win32-${arch}`, 'conpty.node'),
      path.join(unpackedRoot, 'node_modules', '@lydell', `node-pty-win32-${arch}`, 'prebuilds', `win32-${arch}`, 'conpty', 'conpty.dll')
    ]
    for (const required of nativePtyFiles) {
      if (!existsSync(required)) {
        fail(`Missing bundled @lydell/node-pty Windows ${arch} native file ${path.relative(unpackedRoot, required)}.`)
      }
    }
  }

  if ((platform === 'win32' || platform === 'darwin') && arch) {
    verifyVoiceInference(entries, unpackedRoot, platform, arch)
  }

  const requiredResourceFiles = [
    path.join(resourcesDir, 'bin', platform === 'win32' ? 'oratorio-server.exe' : 'oratorio-server'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'agent-teams', '.craft-plugin', 'plugin.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'agent-teams', 'desktop-extensions.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'agent-teams', 'desktop', 'team-card-board.mjs'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'browser', '.craft-plugin', 'plugin.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'chrome', '.craft-plugin', 'plugin.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'chrome', 'scripts', 'extension-id.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'chrome', 'extension', 'manifest.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'oratorio', '.craft-plugin', 'plugin.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'oratorio', 'apps.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'oratorio', 'desktop-extensions.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'oratorio', 'desktop', 'oratorio.mjs')
  ]
  for (const required of requiredResourceFiles) {
    if (!existsSync(required)) {
      fail(`Missing bundled plugin resource ${path.relative(resourcesDir, required)}.`)
    }
  }
  const requiredAsarEntries = [
    'node_modules/@vscode/ripgrep/lib/index.js',
    'node_modules/ignore-walk/lib/index.js',
    'node_modules/@fugood/whisper.node/lib/index.js',
    'out/main/voiceWorker.js'
  ]
  for (const required of requiredAsarEntries) {
    if (!entries.has(required)) {
      fail(`Missing ${required} in app.asar.`)
    }
  }

  verifyDevToolsPolicy(appAsar)

  if (process.exitCode !== 1) {
    console.log(`[verify-package] OK: native runtime files, plugin resources, and file-index JS dependencies are packaged in ${resourcesDir}`)
  }
}

function verifyVoiceInference(entries, unpackedRoot, platform, arch) {
  const packageName = `node-whisper-${platform}-${arch}`
  const packagePrefix = 'node_modules/@fugood/node-whisper-'
  const expectedRoot = `${packagePrefix}${platform}-${arch}`
  const nativeFile = path.join(
    unpackedRoot,
    'node_modules',
    '@fugood',
    packageName,
    'index.node'
  )
  if (!existsSync(nativeFile)) {
    fail(`Missing unpacked Whisper native binding for ${platform}-${arch}.`)
  }

  const unexpectedRuntime = [...entries].find((entry) => (
    entry.startsWith(packagePrefix)
      && entry !== expectedRoot
      && !entry.startsWith(`${expectedRoot}/`)
      && !entry.startsWith('node_modules/@fugood/node-whisper-wasm/')
  ))
  if (unexpectedRuntime) {
    fail(`Unexpected Whisper runtime packaged for ${platform}-${arch}: ${unexpectedRuntime}`)
  }

  const forbiddenPrefixes = [
    'node_modules/@fugood/node-whisper-wasm/',
    'node_modules/@fugood/node-whisper-win32-x64-cuda/',
    'node_modules/@fugood/node-whisper-win32-x64-vulkan/',
    'node_modules/@fugood/node-whisper-win32-arm64-cuda/',
    'node_modules/@fugood/node-whisper-win32-arm64-vulkan/',
    'node_modules/@fugood/node-whisper-linux-x64-cuda/',
    'node_modules/@fugood/node-whisper-linux-x64-vulkan/',
    'node_modules/@fugood/node-whisper-linux-arm64-cuda/',
    'node_modules/@fugood/node-whisper-linux-arm64-vulkan/'
  ]
  for (const prefix of forbiddenPrefixes) {
    if ([...entries].some((entry) => entry.startsWith(prefix))) {
      fail(`Optional Whisper backend must not be packaged: ${prefix}`)
    }
  }
}

function verifyDevToolsPolicy(appAsar) {
  const mainBundle = extractAsarText(appAsar, 'out/main/index.js')

  if (mainBundle.includes('toggleDevTools')) {
    fail('Packaged main process bundle still exposes the toggleDevTools menu role.')
  }

  const devToolsOccurrences = mainBundle.match(/\bdevTools\s*:/g)?.length ?? 0
  if (devToolsOccurrences < 2) {
    fail('Packaged main process bundle does not configure devTools for both BrowserWindow and WebContentsView.')
  }
}

function extractAsarText(appAsar, entryPath) {
  const entries = listPackage(appAsar)
  const matchingEntry = entries.find((entry) => normalizeAsarPath(entry) === entryPath)
  const candidates = matchingEntry
    ? [matchingEntry.replace(/^[\\/]+/, ''), entryPath, entryPath.replace(/\//g, '\\')]
    : [entryPath, entryPath.replace(/\//g, '\\')]

  for (const candidate of candidates) {
    try {
      return extractFile(appAsar, candidate).toString('utf8')
    } catch (error) {
      if (!String(error?.message ?? error).includes('was not found in this archive')) {
        throw error
      }
    }
  }

  fail(`Missing ${entryPath} in app.asar.`)
  return ''
}

function resolveRipgrepPath(unpackedRoot, platform, arch) {
  if (platform === 'win32' && arch) {
    const rg = path.join(unpackedRoot, 'node_modules', '@vscode', `ripgrep-win32-${arch}`, 'bin', 'rg.exe')
    return existsSync(rg) ? rg : null
  }

  const rgCandidates = [
    path.join(unpackedRoot, 'node_modules', '@vscode', 'ripgrep', 'bin', 'rg.exe'),
    path.join(unpackedRoot, 'node_modules', '@vscode', 'ripgrep', 'bin', 'rg')
  ]
  const vscodeModulesDir = path.join(unpackedRoot, 'node_modules', '@vscode')
  if (existsSync(vscodeModulesDir)) {
    for (const packageName of readdirSync(vscodeModulesDir)) {
      if (!packageName.startsWith('ripgrep-')) continue
      rgCandidates.push(
        path.join(vscodeModulesDir, packageName, 'bin', 'rg.exe'),
        path.join(vscodeModulesDir, packageName, 'bin', 'rg')
      )
    }
  }

  return rgCandidates.find((candidate) => existsSync(candidate)) ?? null
}
