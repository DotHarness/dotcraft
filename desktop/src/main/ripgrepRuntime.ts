import { rgPath as rawRgPath } from '@vscode/ripgrep'
import { app } from 'electron'
import * as path from 'path'

export const DOTCRAFT_RG_PATH_ENV = 'DOTCRAFT_RG_PATH'
export const DOTCRAFT_BUILTIN_PLUGIN_ROOTS_ENV = 'DOTCRAFT_BUILTIN_PLUGIN_ROOTS'
export const DOTCRAFT_BUILTIN_PLUGIN_CATALOGS_ENV = 'DOTCRAFT_BUILTIN_PLUGIN_CATALOGS'
export const DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL_ENV = 'DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL'
export const DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL =
  'https://github.com/DotHarness/dotcraft-plugins/archive/refs/heads/main.zip'

export interface DotCraftRuntimeTools {
  ripgrepPath?: string
  nodeBin?: string
  nodeRunAsNode?: boolean
  modulesDir?: string
  builtInPluginRoots?: string
  builtInPluginCatalogs?: string
  defaultPluginRegistryUrl?: string
}

export function resolveBundledRipgrepPath(): string {
  return rawRgPath.replace(/\bapp\.asar\b/g, 'app.asar.unpacked')
}

export function resolveDotCraftRuntimeTools(): DotCraftRuntimeTools {
  return {
    ripgrepPath: resolveBundledRipgrepPath(),
    nodeBin: resolveBundledNodePath(),
    nodeRunAsNode: true,
    modulesDir: resolveBundledModulesDir(),
    builtInPluginRoots: resolveBundledBuiltInPluginRoot(),
    builtInPluginCatalogs: resolveBundledBuiltInPluginCatalog(),
    defaultPluginRegistryUrl: DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL
  }
}

export function buildDotCraftRuntimeEnv(): NodeJS.ProcessEnv {
  return {
    [DOTCRAFT_RG_PATH_ENV]: resolveBundledRipgrepPath(),
    [DOTCRAFT_BUILTIN_PLUGIN_ROOTS_ENV]: resolveBundledBuiltInPluginRoot(),
    [DOTCRAFT_BUILTIN_PLUGIN_CATALOGS_ENV]: resolveBundledBuiltInPluginCatalog(),
    [DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL_ENV]: DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL
  }
}

export function resolveBundledNodePath(): string {
  return process.execPath
}

export function resolveBundledModulesDir(): string {
  if (app.isPackaged) {
    return path.join(process.resourcesPath, 'modules')
  }
  return path.resolve(__dirname, '../../../sdk/typescript/packages')
}

export function resolveBundledBuiltInPluginRoot(): string {
  if (app.isPackaged) {
    return path.join(process.resourcesPath, 'plugins', 'dotcraft-bundled', 'plugins')
  }
  return path.join(app.getAppPath(), 'resources', 'plugins', 'dotcraft-bundled', 'plugins')
}

export function resolveBundledBuiltInPluginCatalog(): string {
  if (app.isPackaged) {
    return path.join(process.resourcesPath, 'plugins', 'dotcraft-bundled', 'catalog.json')
  }
  return path.join(app.getAppPath(), 'resources', 'plugins', 'dotcraft-bundled', 'catalog.json')
}
