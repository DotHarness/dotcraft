import { getIcon } from '@iconify/react'
import {
  areVscodeIconsReady,
  ensureVscodeIcons,
  fileIconName,
  subscribeVscodeIconsReady
} from '../../utils/fileTypeIcons'

/**
 * Paints a colored VS Code file-type icon into a raw DOM element, for the
 * contentEditable composer pill (which cannot mount the React <FileTypeIcon>).
 *
 * The ~1.3 MB icon collection loads lazily, so this paints a neutral fallback first
 * and upgrades in place once the collection registers.
 */
export function paintFileRefIcon(target: HTMLElement, path: string, size = 14): void {
  void ensureVscodeIcons()

  const paintColored = (): boolean => {
    if (!areVscodeIconsReady()) return false
    const data = getIcon(fileIconName(path))
    if (!data) return false
    // getIcon returns a fully-resolved icon (left/top/width/height always set);
    // mirror Iconify's own viewBox so the glyph frames exactly like <FileTypeIcon>.
    const left = data.left ?? 0
    const top = data.top ?? 0
    const width = data.width ?? 16
    const height = data.height ?? 16
    target.innerHTML =
      `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" ` +
      `viewBox="${left} ${top} ${width} ${height}" aria-hidden="true">${data.body}</svg>`
    return true
  }

  if (paintColored()) return

  target.innerHTML = fallbackFileSvg(size)
  const unsubscribe = subscribeVscodeIconsReady(() => {
    if (paintColored()) unsubscribe()
  })
}

function fallbackFileSvg(size: number): string {
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 24 24" ` +
    `fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" ` +
    `aria-hidden="true"><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/>` +
    `<path d="M14 2v4a2 2 0 0 0 2 2h4"/></svg>`
  )
}
