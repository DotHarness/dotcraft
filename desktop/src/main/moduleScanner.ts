import { app } from 'electron'
import { promises as fs } from 'fs'
import type { Dirent } from 'fs'
import * as path from 'path'
import type { AppSettings } from './settings'
import { SUPPORTED_LOCALE_VALUES, type LocalizedTextMap } from '../shared/locales'

export interface ConfigDescriptorWire {
  key: string
  displayLabel: string
  description: string
  localizedDisplayLabel?: LocalizedTextMap
  localizedDescription?: LocalizedTextMap
  required: boolean
  dataKind: string
  masked: boolean
  interactiveSetupOnly: boolean
  group?: string
  advanced?: boolean
  defaultValue?: unknown
  options?: ConfigFieldOptionWire[]
  allowCustomValue?: boolean
  enumValues?: string[]
}

export interface ConfigFieldOptionWire {
  value: string
  displayLabel: string
  localizedDisplayLabel?: LocalizedTextMap
  description?: string
  localizedDescription?: LocalizedTextMap
  preview?: string
}

export interface ConfigGroupDescriptorWire {
  id: string
  displayLabel: string
  localizedDisplayLabel?: LocalizedTextMap
  description?: string
  localizedDescription?: LocalizedTextMap
}

export interface ModuleInterfaceWire {
  shortDescription?: string
  localizedShortDescription?: LocalizedTextMap
  longDescription?: string
  localizedLongDescription?: LocalizedTextMap
  previewPrompt?: string
  localizedPreviewPrompt?: LocalizedTextMap
}

export interface DiscoveredModule {
  moduleId: string
  channelName: string
  displayName: string
  localizedDisplayName?: LocalizedTextMap
  interface?: ModuleInterfaceWire
  packageName: string
  configFileName: string
  supportedTransports: string[]
  requiresInteractiveSetup: boolean
  capabilitySummary?: Record<string, unknown>
  variant: string
  source: 'bundled' | 'user'
  absolutePath: string
  configGroups?: ConfigGroupDescriptorWire[]
  configDescriptors: ConfigDescriptorWire[]
}

export interface ChannelModuleGroup {
  channelName: string
  activeModuleId: string
  modules: DiscoveredModule[]
}

interface ManifestWire {
  moduleId: unknown
  channelName: unknown
  displayName: unknown
  localizedDisplayName?: unknown
  interface?: unknown
  packageName: unknown
  configFileName: unknown
  supportedTransports: unknown
  requiresInteractiveSetup: unknown
  capabilitySummary?: unknown
  variant: unknown
  configGroups?: unknown
  configDescriptors: unknown
}

function asNonEmptyString(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null
}

function asStringArray(value: unknown): string[] | null {
  if (!Array.isArray(value)) return null
  const parsed = value.filter((item): item is string => typeof item === 'string' && item.trim() !== '')
  return parsed.length === value.length ? parsed : null
}

const SUPPORTED_LOCALE_SET = new Set<string>(SUPPORTED_LOCALE_VALUES)

function asLocalizedStringMap(value: unknown): LocalizedTextMap | null {
  if (value == null) return {}
  if (typeof value !== 'object' || Array.isArray(value)) return null
  const record = value as Record<string, unknown>
  const localized: Record<string, string> = {}
  for (const [key, item] of Object.entries(record)) {
    if (!SUPPORTED_LOCALE_SET.has(key)) return null
    if (typeof item !== 'string') return null
    localized[key] = item
  }
  return localized as LocalizedTextMap
}

function asOptionalString(value: unknown): string | undefined | null {
  if (value === undefined) return undefined
  return typeof value === 'string' ? value : null
}

function asPlainObject(value: unknown): Record<string, unknown> | null {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return null
  return value as Record<string, unknown>
}

function parseModuleInterface(value: unknown): ModuleInterfaceWire | undefined | null {
  if (value === undefined) return undefined
  const item = asPlainObject(value)
  if (item === null) return null
  const shortDescription = asOptionalString(item.shortDescription)
  const longDescription = asOptionalString(item.longDescription)
  const previewPrompt = asOptionalString(item.previewPrompt)
  const localizedShortDescription =
    item.localizedShortDescription == null ? undefined : asLocalizedStringMap(item.localizedShortDescription)
  const localizedLongDescription =
    item.localizedLongDescription == null ? undefined : asLocalizedStringMap(item.localizedLongDescription)
  const localizedPreviewPrompt =
    item.localizedPreviewPrompt == null ? undefined : asLocalizedStringMap(item.localizedPreviewPrompt)
  if (
    shortDescription === null ||
    longDescription === null ||
    previewPrompt === null ||
    localizedShortDescription === null ||
    localizedLongDescription === null ||
    localizedPreviewPrompt === null
  ) {
    return null
  }
  const parsed: ModuleInterfaceWire = {}
  if (shortDescription !== undefined) parsed.shortDescription = shortDescription
  if (localizedShortDescription !== undefined) parsed.localizedShortDescription = localizedShortDescription
  if (longDescription !== undefined) parsed.longDescription = longDescription
  if (localizedLongDescription !== undefined) parsed.localizedLongDescription = localizedLongDescription
  if (previewPrompt !== undefined) parsed.previewPrompt = previewPrompt
  if (localizedPreviewPrompt !== undefined) parsed.localizedPreviewPrompt = localizedPreviewPrompt
  return parsed
}

function parseConfigDescriptor(value: unknown): ConfigDescriptorWire | null {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return null
  const item = value as Record<string, unknown>
  const key = asNonEmptyString(item.key)
  const displayLabel = asNonEmptyString(item.displayLabel)
  const description = typeof item.description === 'string' ? item.description : ''
  const localizedDisplayLabel =
    item.localizedDisplayLabel == null ? undefined : asLocalizedStringMap(item.localizedDisplayLabel)
  const localizedDescription =
    item.localizedDescription == null ? undefined : asLocalizedStringMap(item.localizedDescription)
  const dataKind = asNonEmptyString(item.dataKind)
  const group = item.group == null ? undefined : asNonEmptyString(item.group)
  const enumValues = item.enumValues == null ? undefined : asStringArray(item.enumValues)
  const optionsRaw = item.options
  const options: ConfigFieldOptionWire[] = []
  if (optionsRaw !== undefined) {
    if (!Array.isArray(optionsRaw)) return null
    const values = new Set<string>()
    for (const optionRaw of optionsRaw) {
      const option = parseConfigFieldOption(optionRaw)
      if (option === null || values.has(option.value)) return null
      values.add(option.value)
      options.push(option)
    }
  }
  if (
    key === null ||
    displayLabel === null ||
    dataKind === null ||
    group === null ||
    localizedDisplayLabel === null ||
    localizedDescription === null ||
    typeof item.required !== 'boolean' ||
    typeof item.masked !== 'boolean' ||
    typeof item.interactiveSetupOnly !== 'boolean' ||
    (item.enumValues !== undefined && enumValues === null) ||
    (item.allowCustomValue !== undefined && typeof item.allowCustomValue !== 'boolean') ||
    (item.allowCustomValue === true && dataKind !== 'enum')
  ) {
    return null
  }
  return {
    key,
    displayLabel,
    description,
    localizedDisplayLabel,
    localizedDescription,
    required: item.required,
    dataKind,
    masked: item.masked,
    interactiveSetupOnly: item.interactiveSetupOnly,
    group,
    advanced: item.advanced === true,
    defaultValue: item.defaultValue,
    options: optionsRaw === undefined ? undefined : options,
    allowCustomValue: item.allowCustomValue === true ? true : undefined,
    enumValues: enumValues ?? undefined
  }
}

function parseConfigFieldOption(value: unknown): ConfigFieldOptionWire | null {
  const item = asPlainObject(value)
  if (item === null) return null
  const optionValue = asNonEmptyString(item.value)
  const displayLabel = asNonEmptyString(item.displayLabel)
  const description = asOptionalString(item.description)
  const preview = asOptionalString(item.preview)
  const localizedDisplayLabel =
    item.localizedDisplayLabel == null ? undefined : asLocalizedStringMap(item.localizedDisplayLabel)
  const localizedDescription =
    item.localizedDescription == null ? undefined : asLocalizedStringMap(item.localizedDescription)
  if (
    optionValue === null || displayLabel === null || description === null || preview === null ||
    localizedDisplayLabel === null || localizedDescription === null
  ) return null
  return {
    value: optionValue,
    displayLabel,
    localizedDisplayLabel,
    description,
    localizedDescription,
    preview
  }
}

function parseConfigGroup(value: unknown): ConfigGroupDescriptorWire | null {
  const item = asPlainObject(value)
  if (item === null) return null
  const id = asNonEmptyString(item.id)
  const displayLabel = asNonEmptyString(item.displayLabel)
  const description = asOptionalString(item.description)
  const localizedDisplayLabel =
    item.localizedDisplayLabel == null ? undefined : asLocalizedStringMap(item.localizedDisplayLabel)
  const localizedDescription =
    item.localizedDescription == null ? undefined : asLocalizedStringMap(item.localizedDescription)
  if (
    id === null || displayLabel === null || description === null ||
    localizedDisplayLabel === null || localizedDescription === null
  ) return null
  return { id, displayLabel, localizedDisplayLabel, description, localizedDescription }
}

function parseManifest(
  manifest: ManifestWire,
  source: 'bundled' | 'user',
  modulePath: string
): DiscoveredModule | null {
  const moduleId = asNonEmptyString(manifest.moduleId)
  const channelName = asNonEmptyString(manifest.channelName)
  const displayName = asNonEmptyString(manifest.displayName)
  const localizedDisplayName =
    manifest.localizedDisplayName == null ? undefined : asLocalizedStringMap(manifest.localizedDisplayName)
  const moduleInterface = parseModuleInterface(manifest.interface)
  const packageName = asNonEmptyString(manifest.packageName)
  const configFileName = asNonEmptyString(manifest.configFileName)
  const supportedTransports = asStringArray(manifest.supportedTransports)
  const variant = asNonEmptyString(manifest.variant)
  const capabilitySummary =
    manifest.capabilitySummary === undefined ? undefined : asPlainObject(manifest.capabilitySummary)
  const descriptorsRaw = manifest.configDescriptors
  const groupsRaw = manifest.configGroups
  if (
    moduleId === null ||
    channelName === null ||
    displayName === null ||
    localizedDisplayName === null ||
    moduleInterface === null ||
    packageName === null ||
    configFileName === null ||
    supportedTransports === null ||
    capabilitySummary === null ||
    variant === null ||
    typeof manifest.requiresInteractiveSetup !== 'boolean' ||
    !Array.isArray(descriptorsRaw) ||
    (groupsRaw !== undefined && !Array.isArray(groupsRaw))
  ) {
    return null
  }

  const configGroups: ConfigGroupDescriptorWire[] = []
  const groupIds = new Set<string>()
  for (const groupRaw of groupsRaw ?? []) {
    const group = parseConfigGroup(groupRaw)
    if (group === null || groupIds.has(group.id)) return null
    groupIds.add(group.id)
    configGroups.push(group)
  }

  const descriptors: ConfigDescriptorWire[] = []
  for (const descriptorRaw of descriptorsRaw) {
    const descriptor = parseConfigDescriptor(descriptorRaw)
    if (descriptor === null) return null
    if (descriptor.group !== undefined && !groupIds.has(descriptor.group)) return null
    descriptors.push(descriptor)
  }

  const parsed: DiscoveredModule = {
    moduleId,
    channelName,
    displayName,
    localizedDisplayName,
    packageName,
    configFileName,
    supportedTransports,
    requiresInteractiveSetup: manifest.requiresInteractiveSetup,
    variant,
    source,
    absolutePath: modulePath,
    configDescriptors: descriptors
  }
  if (groupsRaw !== undefined) parsed.configGroups = configGroups
  if (moduleInterface !== undefined) parsed.interface = moduleInterface
  if (capabilitySummary !== undefined) parsed.capabilitySummary = capabilitySummary
  return parsed
}

function bundledModulesDir(isDev: boolean): string {
  if (isDev) {
    return path.resolve(__dirname, '../../../sdk/typescript/packages')
  }
  return path.join(process.resourcesPath, 'modules')
}

function userModulesDir(settings: AppSettings): string {
  return settings.modulesDirectory ?? path.join(app.getPath('home'), '.craft', 'modules')
}

function activeVariantKey(channelName: string): string {
  return channelName.trim().toLowerCase()
}

export function groupModulesByChannel(
  modules: DiscoveredModule[],
  activeModuleVariants?: Record<string, string>
): ChannelModuleGroup[] {
  const byChannel = new Map<string, DiscoveredModule[]>()
  for (const module of modules) {
    const key = activeVariantKey(module.channelName)
    const list = byChannel.get(key)
    if (list) {
      list.push(module)
    } else {
      byChannel.set(key, [module])
    }
  }

  const groups: ChannelModuleGroup[] = []
  for (const [channelKey, channelModules] of byChannel) {
    const persistedActive = activeModuleVariants?.[channelKey]
    const persistedMatch =
      persistedActive == null
        ? undefined
        : channelModules.find((module) => module.moduleId === persistedActive)
    const userPreferred = channelModules.find((module) => module.source === 'user')
    const active = persistedMatch ?? userPreferred ?? channelModules[0]
    groups.push({
      channelName: active?.channelName ?? channelModules[0]?.channelName ?? channelKey,
      activeModuleId: active?.moduleId ?? channelModules[0]?.moduleId ?? '',
      modules: channelModules
    })
  }

  return groups
}

async function scanSingleRoot(
  rootDir: string,
  source: 'bundled' | 'user'
): Promise<DiscoveredModule[]> {
  const discovered: DiscoveredModule[] = []
  let entries: Dirent[]
  try {
    entries = await fs.readdir(rootDir, { withFileTypes: true })
  } catch {
    return discovered
  }

  for (const entry of entries) {
    if (!entry.isDirectory()) continue
    const modulePath = path.join(rootDir, entry.name)
    const manifestPath = path.join(modulePath, 'manifest.json')
    try {
      const raw = await fs.readFile(manifestPath, 'utf-8')
      const parsed = JSON.parse(raw) as ManifestWire
      const module = parseManifest(parsed, source, modulePath)
      if (module === null) {
        console.warn(`[moduleScanner] invalid manifest: ${manifestPath}`)
        continue
      }
      discovered.push(module)
    } catch (error) {
      const code = (error as NodeJS.ErrnoException | null)?.code
      if (code !== 'ENOENT') {
        console.warn(`[moduleScanner] failed to load manifest: ${manifestPath}`, error)
      }
    }
  }

  return discovered
}

export async function scanModules(settings: AppSettings, isDev: boolean): Promise<DiscoveredModule[]> {
  const bundled = await scanSingleRoot(bundledModulesDir(isDev), 'bundled')
  const user = await scanSingleRoot(userModulesDir(settings), 'user')
  const merged = new Map<string, DiscoveredModule>()

  for (const module of bundled) {
    merged.set(module.moduleId, module)
  }
  for (const module of user) {
    merged.set(module.moduleId, module)
  }

  return [...merged.values()]
}
