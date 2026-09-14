import { app } from 'electron'
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import nodeProcess from 'node:process'
import { pathToFileURL } from 'node:url'
import { checkChromeSetup, resolveChromePluginRoot, runChromeSetupScript } from '../chromeSetup'

function resolveBrowserClientPath(): string {
  const dev = join(app.getAppPath(), 'resources', 'browser', 'scripts', 'browser-client.mjs')
  if (existsSync(dev)) return pathToFileURL(dev).href
  const cwdDev = join(nodeProcess.cwd(), 'resources', 'browser', 'scripts', 'browser-client.mjs')
  if (existsSync(cwdDev)) return pathToFileURL(cwdDev).href

  const resourcesPath = nodeProcess.resourcesPath
  if (resourcesPath) {
    const packaged = join(resourcesPath, 'browser', 'scripts', 'browser-client.mjs')
    if (existsSync(packaged)) return pathToFileURL(packaged).href
  }

  return pathToFileURL(dev).href
}

function resolveChromeBrowserClientPath(): string {
  const dev = join(app.getAppPath(), 'resources', 'chrome', 'browser-client.mjs')
  if (existsSync(dev)) return pathToFileURL(dev).href
  const cwdDev = join(nodeProcess.cwd(), 'resources', 'chrome', 'browser-client.mjs')
  if (existsSync(cwdDev)) return pathToFileURL(cwdDev).href

  const resourcesPath = nodeProcess.resourcesPath
  if (resourcesPath) {
    const packaged = join(resourcesPath, 'chrome', 'browser-client.mjs')
    if (existsSync(packaged)) return pathToFileURL(packaged).href
  }

  return pathToFileURL(dev).href
}

export function createReplHostContext(workspacePath: string, browserSession: Record<string, unknown>): Record<string, unknown> {
  const chromePluginRoot = resolveChromePluginRoot(workspacePath)
  return {
    browserClientPath: resolveBrowserClientPath(), chromeBrowserClientPath: resolveChromeBrowserClientPath(),
    workspacePath, browserSession, chromePluginRoot, chromeScriptsPath: join(chromePluginRoot, 'scripts')
  }
}

export async function handleChromeHostCall(method: string, workspacePath: string): Promise<unknown> {
  switch (method) {
    case 'chrome.checkSetup': return await checkChromeSetup(workspacePath)
    case 'chrome.checkExtension': return await runChromeSetupScript(workspacePath, 'check-extension-installed.js', ['--json'])
    case 'chrome.checkNativeHost': return await runChromeSetupScript(workspacePath, 'check-native-host-manifest.js', ['--json'])
    default: throw new Error(`Unknown Node REPL host method: ${method}`)
  }
}
