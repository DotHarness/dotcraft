import { promises as fs } from 'fs'
import * as path from 'path'
import { findMissingRequiredConfigFields } from '../shared/channelModuleConfig'
import type {
  DiscoveredModule,
  ModuleConfigStatus,
  ModuleConfigStatusMap
} from '../shared/channelModules'
import { parseJsonObjectConfig } from '../shared/jsonConfig'

/** An unreadable config file counts as not configured, so the UI can offer setup. */
async function readModuleConfigStatus(
  workspacePath: string,
  module: DiscoveredModule
): Promise<ModuleConfigStatus> {
  const configPath = path.join(workspacePath, '.craft', module.configFileName)
  try {
    const config = parseJsonObjectConfig(await fs.readFile(configPath, 'utf-8'))
    return {
      exists: true,
      missingRequired: findMissingRequiredConfigFields(config, module.configDescriptors)
    }
  } catch {
    return {
      exists: false,
      missingRequired: findMissingRequiredConfigFields({}, module.configDescriptors)
    }
  }
}

export async function readModuleConfigStatusMap(
  workspacePath: string,
  modules: DiscoveredModule[]
): Promise<ModuleConfigStatusMap> {
  const entries = await Promise.all(
    modules.map(
      async (module) =>
        [module.moduleId, await readModuleConfigStatus(workspacePath, module)] as const
    )
  )
  return Object.fromEntries(entries)
}
