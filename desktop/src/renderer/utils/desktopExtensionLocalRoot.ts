import type { DesktopMainViewExtension } from './desktopExtensionRegistry'

export function remapDesktopExtensionToLocalRoot(
  entry: DesktopMainViewExtension,
  localRootPath: string
): DesktopMainViewExtension {
  const requestedRootPath = entry.plugin.rootPath
  if (normalizePath(requestedRootPath) === normalizePath(localRootPath)) return entry

  return {
    ...entry,
    plugin: {
      ...entry.plugin,
      rootPath: localRootPath
    },
    extension: {
      ...entry.extension,
      entry: remapPluginPath(entry.extension.entry, requestedRootPath, localRootPath),
      styles: entry.extension.styles.map((stylePath) =>
        remapPluginPath(stylePath, requestedRootPath, localRootPath))
    }
  }
}

function remapPluginPath(value: string, requestedRootPath: string, localRootPath: string): string {
  const normalizedValue = normalizePath(value)
  const normalizedRequestedRoot = normalizePath(requestedRootPath)
  const relative = normalizedValue === normalizedRequestedRoot
    ? ''
    : normalizedValue.startsWith(`${normalizedRequestedRoot}/`)
      ? normalizedValue.slice(normalizedRequestedRoot.length + 1)
      : null
  if (relative == null) {
    throw new Error('Desktop extension path must stay inside the reported plugin root.')
  }
  return relative ? `${trimEndingSeparators(localRootPath)}/${relative}` : trimEndingSeparators(localRootPath)
}

function normalizePath(value: string): string {
  return trimEndingSeparators(value).replace(/\\/g, '/')
}

function trimEndingSeparators(value: string): string {
  return value.replace(/[\\/]+$/, '')
}
