import { existsSync, readdirSync, statSync } from 'fs'
import path from 'path'
import { listPackage } from '@electron/asar'

const cwd = process.cwd()
const distDir = path.join(cwd, 'dist')

function normalizeAsarPath(value) {
  return value.replace(/\\/g, '/').replace(/^\/+/, '')
}

function existingResourcesDirs() {
  const candidates = [
    path.join(distDir, 'win-unpacked', 'resources'),
    path.join(distDir, 'linux-unpacked', 'resources')
  ]

  if (existsSync(distDir)) {
    for (const name of readdirSync(distDir)) {
      const maybeMacDir = path.join(distDir, name)
      if (!statSync(maybeMacDir).isDirectory()) continue
      const appDir = path.join(maybeMacDir, 'DotCraft.app', 'Contents', 'Resources')
      if (existsSync(appDir)) candidates.push(appDir)
    }
  }

  return candidates.filter((candidate) => existsSync(path.join(candidate, 'app.asar')))
}

function fail(message) {
  console.error(`[verify-package] ${message}`)
  process.exitCode = 1
}

const resourcesDirs = existingResourcesDirs()
if (resourcesDirs.length === 0) {
  fail('No packaged Electron resources directory found under dist/. Run electron-builder first.')
} else {
  const resourcesDir = resourcesDirs[0]
  const appAsar = path.join(resourcesDir, 'app.asar')
  const unpackedRoot = path.join(resourcesDir, 'app.asar.unpacked')
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
  const rgPath = rgCandidates.find((candidate) => existsSync(candidate))

  if (!rgPath) {
    fail('Missing unpacked @vscode/ripgrep binary under app.asar.unpacked.')
  }

  const requiredResourceFiles = [
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'browser', '.craft-plugin', 'plugin.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'chrome', '.craft-plugin', 'plugin.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'chrome', 'scripts', 'extension-id.json'),
    path.join(resourcesDir, 'plugins', 'dotcraft-bundled', 'plugins', 'chrome', 'extension', 'manifest.json')
  ]
  for (const required of requiredResourceFiles) {
    if (!existsSync(required)) {
      fail(`Missing bundled plugin resource ${path.relative(resourcesDir, required)}.`)
    }
  }

  const entries = new Set(listPackage(appAsar).map(normalizeAsarPath))
  const requiredAsarEntries = [
    'node_modules/@vscode/ripgrep/lib/index.js',
    'node_modules/ignore-walk/lib/index.js'
  ]
  for (const required of requiredAsarEntries) {
    if (!entries.has(required)) {
      fail(`Missing ${required} in app.asar.`)
    }
  }

  if (process.exitCode !== 1) {
    console.log(`[verify-package] OK: ripgrep, plugin resources, and file-index JS dependencies are packaged in ${resourcesDir}`)
  }
}
