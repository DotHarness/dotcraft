import { cpSync, mkdirSync, rmSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'

const desktopRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const builder = resolve(
  desktopRoot,
  'node_modules/@dotcraft/plugin/scripts/build-plugin.mjs'
)

for (const pluginId of ['agent-teams', 'dotcraft', 'oratorio']) {
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
}
