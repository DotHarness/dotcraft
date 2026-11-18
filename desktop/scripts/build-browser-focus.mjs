import { spawnSync } from 'node:child_process'
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const desktop = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const source = path.join(desktop, 'native', 'browser-focus')
const output = path.join(desktop, 'resources', 'native', 'browser-focus.node')
const license = path.join(desktop, 'resources', 'native', 'browser-focus-LICENSES.txt')
const [platform = process.platform, arch = process.arch] = process.argv.slice(2)

if (platform !== 'win32' || arch !== 'x64') {
  await rm(output, { force: true })
  await rm(license, { force: true })
  console.log(`[browser-focus] No native module for ${platform}-${arch}.`)
  process.exit(0)
}
if (process.platform !== 'win32' || process.arch !== 'x64') {
  throw new Error('The Windows x64 browser focus module must be built on Windows x64.')
}

const require = createRequire(import.meta.url)
const { version } = require('electron/package.json')
if (version !== '42.0.1') {
  throw new Error(`Browser focus isolation requires Electron 42.0.1, found ${version}.`)
}

const vswhere = path.join(process.env['ProgramFiles(x86)'], 'Microsoft Visual Studio', 'Installer', 'vswhere.exe')
const visualStudio = spawnSync(vswhere, [
  '-latest', '-products', '*',
  '-requires', 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64',
  '-property', 'installationPath'
], { encoding: 'utf8' })
if (visualStudio.error || visualStudio.status !== 0 || !visualStudio.stdout.trim()) {
  throw new Error('Install Visual Studio Build Tools with Desktop development with C++.')
}

const build = path.join(source, 'build')
await mkdir(build, { recursive: true })
await mkdir(path.dirname(output), { recursive: true })
const devCommand = path.join(visualStudio.stdout.trim(), 'Common7', 'Tools', 'VsDevCmd.bat')
const minhook = path.join(source, 'vendor', 'minhook')
const quote = value => `"${value}"`
const cSources = ['buffer.c', 'hook.c', 'trampoline.c', 'hde/hde64.c']
  .map(file => quote(path.join(minhook, 'src', file))).join(' ')
const objects = ['buffer.obj', 'hook.obj', 'trampoline.obj', 'hde64.obj'].join(' ')
const script = path.join(build, 'build.cmd')
await writeFile(script, [
  '@echo off',
  `call ${quote(devCommand)} -arch=x64 -host_arch=x64`,
  'if errorlevel 1 exit /b 1',
  `cl /nologo /c /O2 /MT /W3 /I${quote(path.join(minhook, 'include'))} ${cSources}`,
  'if errorlevel 1 exit /b 1',
  `cl /nologo /LD /std:c++20 /EHsc /O2 /MT /W4 /I${quote(path.join(source, 'vendor', 'node'))} ${quote(path.join(source, 'focus-hook.cpp'))} ${objects} /link /OUT:${quote(output)} /IMPLIB:browser-focus.lib /INCREMENTAL:NO /Brepro`,
  'exit /b %errorlevel%'
].join('\r\n'))

const result = spawnSync(process.env.ComSpec, ['/d', '/c', `call ${quote(script)}`], {
  cwd: build, stdio: 'inherit', windowsVerbatimArguments: true
})
if (result.error) throw result.error
if (result.status !== 0) throw new Error(`Browser focus build failed (${result.status}).`)

const licenses = await Promise.all([
  readFile(path.join(minhook, 'LICENSE.txt'), 'utf8'),
  readFile(path.join(source, 'vendor', 'node', 'LICENSE'), 'utf8')
])
await writeFile(license, licenses.join('\n\n'))
console.log(`[browser-focus] Built ${output}`)
