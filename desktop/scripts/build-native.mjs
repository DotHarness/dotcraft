import { spawnSync } from 'node:child_process'
import { copyFile, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const desktop = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const native = path.join(desktop, 'native')
const vendor = path.join(native, 'browser-focus', 'vendor')
const build = path.join(native, 'build')
const staged = path.join(build, 'win-x64')
const resources = path.join(desktop, 'resources', 'native')
const modules = [
  ['browser-focus', path.join(native, 'browser-focus', 'focus-hook.cpp')],
  ['tray-inline', path.join(native, 'tray-inline', 'tray-inline.cpp')]
]
const license = 'native-LICENSES.txt'
const artifacts = [...modules.map(([name]) => `${name}.node`), license]
const [platform = process.platform, arch = process.arch] = process.argv.slice(2)

if (platform !== 'win32' || arch !== 'x64') {
  await Promise.all(artifacts.map(file => rm(path.join(resources, file), { force: true })))
  console.log(`[native] No Electron add-ons for ${platform}-${arch}.`)
  process.exit(0)
}
if (process.platform !== 'win32' || process.arch !== 'x64') {
  throw new Error('The Windows x64 Electron add-ons must be built on Windows x64.')
}

const require = createRequire(import.meta.url)
const { version } = require('electron/package.json')
if (version !== '42.0.1') {
  throw new Error(`The native Electron add-ons require Electron 42.0.1, found ${version}.`)
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

await mkdir(staged, { recursive: true })
await mkdir(resources, { recursive: true })
const devCommand = path.join(visualStudio.stdout.trim(), 'Common7', 'Tools', 'VsDevCmd.bat')
const minhook = path.join(vendor, 'minhook')
const nodeHeaders = path.join(vendor, 'node')
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
  ...modules.flatMap(([name, source]) => [
    `cl /nologo /LD /std:c++20 /EHsc /O2 /MT /W4 /I${quote(path.join(minhook, 'include'))} /I${quote(nodeHeaders)} ${quote(source)} ${objects} /link /OUT:${quote(path.join(staged, `${name}.node`))} /IMPLIB:${name}.lib /INCREMENTAL:NO /Brepro`,
    'if errorlevel 1 exit /b 1'
  ]),
  'exit /b 0'
].join('\r\n'))

const result = spawnSync(process.env.ComSpec, ['/d', '/c', `call ${quote(script)}`], {
  cwd: build, stdio: 'inherit', windowsVerbatimArguments: true
})
if (result.error) throw result.error
if (result.status !== 0) throw new Error(`Native Electron build failed (${result.status}).`)

const licenses = await Promise.all([
  readFile(path.join(minhook, 'LICENSE.txt'), 'utf8'),
  readFile(path.join(nodeHeaders, 'LICENSE'), 'utf8')
])
await writeFile(path.join(staged, license), licenses.join('\n\n'))
await Promise.all(artifacts.map(file => copyFile(path.join(staged, file), path.join(resources, file))))
console.log(`[native] Built ${modules.map(([name]) => name).join(', ')} for ${platform}-${arch}.`)
