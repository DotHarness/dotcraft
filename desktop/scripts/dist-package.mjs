import { spawnSync } from 'child_process'
import path from 'path'
import { fileURLToPath } from 'url'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const desktopDir = path.resolve(scriptDir, '..')

function run(command, args) {
  const result = spawnSync(command, args, {
    cwd: desktopDir,
    stdio: 'inherit',
    shell: false
  })
  if (result.error) {
    console.error(`[dist-package] Failed to run ${command}: ${result.error.message}`)
    process.exit(1)
  }
  if (result.status !== 0) {
    process.exit(result.status ?? 1)
  }
}

function runNpm(args) {
  const npmCli = process.env.npm_execpath
  if (npmCli) {
    run(process.execPath, [npmCli, ...args])
  } else {
    run(process.platform === 'win32' ? 'npm.cmd' : 'npm', args)
  }
}

runNpm(['run', 'ensure:electron'])
const target = resolveTarget(process.argv.slice(2))
run(process.execPath, [
  './scripts/install-target-optional-deps.mjs',
  target.platform,
  target.arch
])
runNpm(['run', 'build'])
run(process.execPath, [
  '--require',
  './scripts/electron-builder-rename-retry.cjs',
  './node_modules/electron-builder/cli.js',
  ...process.argv.slice(2),
  '--publish',
  'never'
])
runNpm(['run', 'verify:package'])

function resolveTarget(args) {
  const platform = args.includes('--win')
    ? 'win32'
    : args.includes('--mac')
      ? 'darwin'
      : process.platform
  const arch = args.includes('--arm64')
    ? 'arm64'
    : args.includes('--x64')
      ? 'x64'
      : process.arch
  return { platform, arch }
}
