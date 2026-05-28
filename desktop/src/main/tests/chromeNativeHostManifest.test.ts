import { afterEach, describe, expect, it } from 'vitest'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { pathToFileURL } from 'node:url'

const chromeScriptsRoot = path.resolve(process.cwd(), 'resources/plugins/dotcraft-bundled/plugins/chrome/scripts')
const checkScriptPath = path.join(chromeScriptsRoot, 'check-native-host-manifest.js')
const installScriptPath = path.join(chromeScriptsRoot, 'installManifest.mjs')
const metadata = JSON.parse(fs.readFileSync(path.join(chromeScriptsRoot, 'extension-id.json'), 'utf8')) as {
  extensionHostName: string
  extensionId: string
}
const NATIVE_HOST_TEST_TIMEOUT_MS = 20_000

const tempDirs: string[] = []

afterEach(() => {
  while (tempDirs.length > 0) {
    const dir = tempDirs.pop()
    if (dir) fs.rmSync(dir, { recursive: true, force: true })
  }
})

function makeTempDir(): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dotcraft-native-host-test-'))
  tempDirs.push(dir)
  return dir
}

function writeManifest(manifestPath: string, hostPath: string): void {
  fs.writeFileSync(manifestPath, `${JSON.stringify({
    name: metadata.extensionHostName,
    description: 'DotCraft Chrome native messaging host',
    type: 'stdio',
    path: hostPath,
    allowed_origins: [`chrome-extension://${metadata.extensionId}/`]
  }, null, 2)}\n`)
}

function runCheck(manifestPath: string): { exitCode: number; result: Record<string, unknown> } {
  try {
    const stdout = execFileSync(process.execPath, [checkScriptPath, '--json'], {
      encoding: 'utf8',
      env: {
        ...process.env,
        DOTCRAFT_CHROME_NATIVE_HOST_MANIFEST_PATH: manifestPath
      }
    })
    return { exitCode: 0, result: JSON.parse(stdout) as Record<string, unknown> }
  } catch (error) {
    const failure = error as { status?: number; stdout?: string | Buffer }
    return {
      exitCode: failure.status ?? 1,
      result: JSON.parse(String(failure.stdout ?? '{}')) as Record<string, unknown>
    }
  }
}

describe('Chrome native host manifest helpers', () => {
  it('generates wrappers that run packaged Electron as Node', async () => {
    const stdout = execFileSync(process.execPath, ['--input-type=module', '--eval', `
      import { windowsWrapperContent, shellWrapperContent } from ${JSON.stringify(pathToFileURL(installScriptPath).href)};
      process.stdout.write(JSON.stringify({
        windowsWrapper: windowsWrapperContent('C:\\\\DotCraft.exe', 'D:\\\\chrome\\\\native-host.mjs'),
        shellWrapper: shellWrapperContent('/opt/DotCraft', '/opt/chrome/native-host.mjs')
      }));
    `], { encoding: 'utf8' })
    const { windowsWrapper, shellWrapper } = JSON.parse(stdout) as {
      windowsWrapper: string
      shellWrapper: string
    }

    expect(windowsWrapper).toContain('set ELECTRON_RUN_AS_NODE=1')
    expect(windowsWrapper).toContain('"C:\\DotCraft.exe" "D:\\chrome\\native-host.mjs"')

    expect(shellWrapper).toContain('ELECTRON_RUN_AS_NODE=1 exec')
    expect(shellWrapper).toContain('"/opt/DotCraft" "/opt/chrome/native-host.mjs"')
  }, NATIVE_HOST_TEST_TIMEOUT_MS)

  it('fails old wrappers that omit ELECTRON_RUN_AS_NODE', () => {
    const dir = makeTempDir()
    const manifestPath = path.join(dir, 'native-host.json')
    const hostPath = path.join(dir, process.platform === 'win32' ? 'dotcraft-chrome-host.cmd' : 'dotcraft-chrome-host.sh')
    fs.writeFileSync(hostPath, `"${process.execPath}" "${path.join(chromeScriptsRoot, 'native-host.mjs')}"\n`)
    writeManifest(manifestPath, hostPath)

    const { exitCode, result } = runCheck(manifestPath)

    expect(exitCode).toBe(1)
    expect(result).toMatchObject({
      ok: false,
      code: 'nativeHostNeedsRepair',
      exists: true,
      hostExists: true,
      wrapperValid: false,
      wrapperPointsToNativeHost: true,
      wrapperHasElectronRunAsNode: false
    })
    expect(result).not.toHaveProperty('manifestPath')
    expect(result).not.toHaveProperty('hostPath')
  }, NATIVE_HOST_TEST_TIMEOUT_MS)

  it('passes wrappers that point to native-host.mjs and set ELECTRON_RUN_AS_NODE', () => {
    const dir = makeTempDir()
    const manifestPath = path.join(dir, 'native-host.json')
    const hostPath = path.join(dir, process.platform === 'win32' ? 'dotcraft-chrome-host.cmd' : 'dotcraft-chrome-host.sh')
    fs.writeFileSync(hostPath, `@echo off\r\nset ELECTRON_RUN_AS_NODE=1\r\n"${process.execPath}" "${path.join(chromeScriptsRoot, 'native-host.mjs')}"\r\n`)
    writeManifest(manifestPath, hostPath)

    const { exitCode, result } = runCheck(manifestPath)

    expect(exitCode).toBe(0)
    expect(result).toMatchObject({
      ok: true,
      code: 'nativeHostReady',
      exists: true,
      hostExists: true,
      wrapperValid: true
    })
  }, NATIVE_HOST_TEST_TIMEOUT_MS)

  it('reports a missing manifest as installable without paths', () => {
    const dir = makeTempDir()
    const manifestPath = path.join(dir, 'missing.json')

    const { exitCode, result } = runCheck(manifestPath)

    expect(exitCode).toBe(1)
    expect(result).toMatchObject({
      ok: false,
      code: 'nativeHostMissing',
      exists: false,
      hostExists: false,
      wrapperValid: false
    })
    expect(result).not.toHaveProperty('manifestPath')
    expect(result).not.toHaveProperty('hostPath')
  }, NATIVE_HOST_TEST_TIMEOUT_MS)
})
