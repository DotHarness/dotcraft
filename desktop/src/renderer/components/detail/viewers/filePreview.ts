import type { ViewerContentClass } from '../../../../shared/viewer/types'

export interface FilePreviewProps {
  absolutePath: string
  relativePath: string
  tabId: string
}

const MIB = 1024 * 1024

const PREVIEW_SIZE_LIMITS: Partial<Record<ViewerContentClass, number>> = {
  spreadsheet: 40 * MIB,
  presentation: 40 * MIB,
  video: 256 * MIB
}

export type PreviewUnavailableReason =
  | { kind: 'type' }
  | { kind: 'size'; sizeBytes: number; limitBytes: number }
  | { kind: 'load'; onRetry: () => void }

export function previewUnavailableReason(
  contentClass: ViewerContentClass,
  sizeBytes: number | undefined
): PreviewUnavailableReason | null {
  if (contentClass === 'unsupported') return { kind: 'type' }
  const limitBytes = PREVIEW_SIZE_LIMITS[contentClass]
  if (limitBytes !== undefined && sizeBytes !== undefined && sizeBytes > limitBytes) {
    return { kind: 'size', sizeBytes, limitBytes }
  }
  return null
}

export function formatPreviewSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const [value, unit] = bytes < MIB ? [bytes / 1024, 'KB'] : bytes < 1024 * MIB ? [bytes / MIB, 'MB'] : [bytes / (1024 * MIB), 'GB']
  return `${Number(value.toFixed(value < 10 ? 1 : 0))} ${unit}`
}

export function fileTypeLabel(path: string): string | null {
  const name = path.replace(/\\/g, '/').split('/').pop() ?? path
  const dot = name.lastIndexOf('.')
  return dot > 0 && dot < name.length - 1 ? name.slice(dot + 1).toUpperCase() : null
}
