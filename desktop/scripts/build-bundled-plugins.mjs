import { cpSync, existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs'
import { dirname, extname, isAbsolute, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'

const desktopRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const builder = resolve(
  desktopRoot,
  'node_modules/@dotcraft/plugin/scripts/build-plugin.mjs'
)

function validateDesktopAsset(pluginRoot, assetPath, extension, field) {
  if (typeof assetPath !== 'string' || assetPath.length === 0) {
    throw new Error(`${pluginRoot}: desktop.${field} must be a non-empty path`)
  }

  const asset = resolve(pluginRoot, assetPath)
  const relativeAsset = relative(pluginRoot, asset)
  const desktopDist = `desktop${sep}dist${sep}`
  if (
    isAbsolute(relativeAsset) ||
    relativeAsset.startsWith(`..${sep}`) ||
    !relativeAsset.startsWith(desktopDist) ||
    extname(asset) !== extension ||
    !existsSync(asset)
  ) {
    throw new Error(
      `${pluginRoot}: desktop.${field} must reference an existing ${extension} file inside ./desktop/dist/`
    )
  }
}

function validateDesktopManifest(pluginRoot) {
  const manifestPath = resolve(pluginRoot, '.craft-plugin/plugin.json')
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'))
  if (!manifest.desktop) return

  validateDesktopAsset(pluginRoot, manifest.desktop.entry, '.mjs', 'entry')
  for (const [index, style] of (manifest.desktop.styles ?? []).entries()) {
    validateDesktopAsset(pluginRoot, style, '.css', `styles[${index}]`)
  }
}

for (const pluginId of ['dotcraft', 'oratorio', 'token-hud', 'wallpaper']) {
  const sourceRoot = resolve(desktopRoot, 'src/bundled-plugins', pluginId)
  const sourceDist = resolve(sourceRoot, 'dist')
  const resourceDist = resolve(
    desktopRoot,
    'resources/plugins/dotcraft-bundled/plugins',
    pluginId,
    'desktop/dist'
  )
  const build = spawnSync(process.execPath, [builder, 'build', sourceRoot], {
    cwd: desktopRoot,
    stdio: 'inherit'
  })
  if (build.status !== 0) {
    process.exit(build.status ?? 1)
  }

  rmSync(resourceDist, { recursive: true, force: true })
  mkdirSync(dirname(resourceDist), { recursive: true })
  cpSync(sourceDist, resourceDist, { recursive: true })
  validateDesktopManifest(resolve(resourceDist, '../..'))
}
