import { VISUALIZATION_MAX_DOWNLOAD_BYTES } from '../../../shared/mcpAppSandbox'
import { normalizeHexColor } from '../../../shared/themeSeed'
import type { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import type { PopupAnchor } from '../ui/anchoredPopup'
import { requestColorPickerDialog } from '../ui/ColorPickerDialog'
import { requestSelectMenu, type SelectMenuRequestOptions } from '../ui/SelectMenu'
import type { ConfirmDialogOptions } from '../ui/ConfirmDialog'
import type { VisualizationThemeTokens } from './visualizationSecurity'

const MAX_LINK_LENGTH = 2048
const MAX_DOWNLOAD_NAME_LENGTH = 200
const MAX_POPUP_LABEL_LENGTH = 80
const MAX_SELECT_OPTIONS = 500
const MAX_SELECT_TEXT_LENGTH = 200

export type VisualizationDownload =
  | { kind: 'file'; name: string; data: Uint8Array }
  | { kind: 'tooLarge' }
  | { kind: 'failed' }

export function readVisualizationThemeTokens(): VisualizationThemeTokens {
  const root = document.documentElement
  const styles = getComputedStyle(root)
  const read = (name: string, fallback: string): string => styles.getPropertyValue(name).trim() || fallback
  return {
    background: read('--bg-primary', 'rgb(24 24 24)'),
    foreground: read('--text-primary', 'rgb(255 255 255)'),
    card: read('--bg-secondary', 'rgb(38 38 38)'),
    cardForeground: read('--text-primary', 'rgb(255 255 255)'),
    primary: read('--accent', 'rgb(131 195 255)'),
    primaryForeground: read('--bg-primary', 'rgb(24 24 24)'),
    secondary: read('--bg-secondary', 'rgb(38 38 38)'),
    secondaryForeground: read('--text-primary', 'rgb(255 255 255)'),
    muted: read('--bg-tertiary', 'rgb(48 48 48)'),
    mutedForeground: read('--text-secondary', 'rgb(184 184 184)'),
    accent: read('--bg-active', 'rgb(54 54 54)'),
    accentForeground: read('--text-primary', 'rgb(255 255 255)'),
    border: read('--border-default', 'rgb(255 255 255 / 12%)'),
    input: read('--border-active', 'rgb(255 255 255 / 20%)'),
    ring: read('--accent', 'rgb(131 195 255)'),
    popover: read('--tooltip-bg', '#ededed'),
    popoverForeground: read('--tooltip-text', '#1a1c1f'),
    destructive: read('--error-text', '#f87171'),
    primaryHover: read('--accent-hover', 'rgb(131 195 255)'),
    onPrimary: read('--on-accent', '#ffffff'),
    dimmedForeground: read('--text-dimmed', 'rgb(112 112 112)'),
    borderSubtle: read('--border-subtle', 'rgb(255 255 255 / 6%)'),
    elevated: read('--bg-elevated', 'rgb(42 42 42)'),
    warning: read('--warning', '#eab308'),
    shadowSm: read('--shadow-sm', '0 1px 3px rgb(0 0 0 / 22%)'),
    shadowMd: read('--shadow-md', '0 8px 22px rgb(0 0 0 / 26%)'),
    tooltipShadow: read('--tooltip-shadow', '0 18px 48px rgb(0 0 0 / 32%)'),
    tooltipRadius: read('--tooltip-radius', '8px'),
    controlHeight: read('--button-height', '32px'),
    controlHeightSm: read('--button-height-sm', '28px'),
    controlRadius: read('--button-radius', '8px'),
    controlTextRadius: read('--button-text-radius', '999px'),
    controlPaddingX: read('--button-padding-x', '12px'),
    fontSizeControl: read('--type-ui-size', '13px'),
    lineHeightControl: read('--type-ui-line-height', '18px'),
    fontSizeSmall: read('--type-secondary-size', '12px'),
    lineHeightSmall: read('--type-secondary-line-height', '16px'),
    fontMono: read('--font-mono', 'ui-monospace, Consolas, monospace'),
    scrollbarSize: read('--scrollbar-size', '12px'),
    scrollbarThumbInset: read('--scrollbar-thumb-inset', '3px'),
    scrollbarThumb: read('--scrollbar-thumb', 'rgb(255 255 255 / 20%)'),
    scrollbarThumbHover: read('--scrollbar-thumb-hover', 'rgb(255 255 255 / 36%)'),
    scrollbarThumbActive: read('--scrollbar-thumb-active', 'rgb(255 255 255 / 60%)'),
    blue: read('--chart-series-1', '#3987e5'),
    orange: read('--chart-series-2', '#d95926'),
    green: read('--chart-series-3', '#199e70'),
    red: read('--error', '#ef4444'),
    purple: read('--ansi-magenta', '#c678dd'),
    yellow: read('--chart-series-4', '#c98500'),
    fontFamily: read('--font-ui', '-apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif'),
    fontSizeBase: read('--conversation-font-size', '14px'),
    cursorInteraction: root.getAttribute('data-pointer-cursors') === 'false' ? 'default' : 'pointer'
  }
}

export function readVisualizationLinkUrl(value: unknown): string | null {
  if (typeof value !== 'string' || value.length > MAX_LINK_LENGTH) return null
  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null
  } catch {
    return null
  }
}

export async function openExternalLinkAfterConfirmation(
  url: string,
  confirm: (options: ConfirmDialogOptions) => Promise<boolean>,
  t: ReturnType<typeof useT>
): Promise<void> {
  const accepted = await confirm({
    title: t('visualization.openLinkTitle'),
    message: url,
    confirmLabel: t('visualization.openLink'),
    cancelLabel: t('common.cancel')
  })
  if (accepted) await window.api.shell.openExternal(url).catch(() => addToast(t('visualization.openLinkFailed'), 'error', 3000))
}

export function readVisualizationDownload(params: Record<string, unknown> | undefined): VisualizationDownload {
  const name = typeof params?.name === 'string' ? params.name.trim().slice(0, MAX_DOWNLOAD_NAME_LENGTH) : ''
  if (typeof params?.size === 'number' && params.size > VISUALIZATION_MAX_DOWNLOAD_BYTES) return { kind: 'tooLarge' }
  if (!name || typeof params?.data !== 'string' || params.data.length === 0) return { kind: 'failed' }
  if (params.data.length > Math.ceil(VISUALIZATION_MAX_DOWNLOAD_BYTES / 3) * 4) return { kind: 'tooLarge' }
  try {
    const binary = atob(params.data)
    const data = new Uint8Array(binary.length)
    for (let index = 0; index < binary.length; index++) data[index] = binary.charCodeAt(index)
    return data.byteLength > VISUALIZATION_MAX_DOWNLOAD_BYTES ? { kind: 'tooLarge' } : { kind: 'file', name, data }
  } catch {
    return { kind: 'failed' }
  }
}

export function openVisualizationPopup(
  method: 'visualization/pickColor' | 'visualization/selectMenu',
  params: Record<string, unknown> | undefined,
  frame: HTMLIFrameElement | null,
  colorTitle: string,
  post: (method: string, params: Record<string, unknown>) => void
): { result: Promise<Record<string, unknown> | null>; dismiss(): void } | null {
  const anchor = readFrameAnchor(params?.rect, frame)
  if (!anchor) return null
  const label = typeof params?.label === 'string' ? params.label.trim().slice(0, MAX_POPUP_LABEL_LENGTH) : ''
  if (method === 'visualization/pickColor') {
    const color = normalizeHexColor(params?.color)
    if (!color) return null
    const picker = requestColorPickerDialog({ title: label || colorTitle, initialColor: color }, {
      anchor,
      onPreview: preview => post('visualization/colorPreview', { color: preview })
    })
    return { result: picker.result.then(result => result.kind === 'select' ? { color: result.color } : null), dismiss: picker.dismiss }
  }
  const options = readSelectOptions(params?.options)
  if (!options) return null
  const selectedIndex = typeof params?.selectedIndex === 'number' && Number.isInteger(params.selectedIndex) ? params.selectedIndex : -1
  const menu = requestSelectMenu({ anchor, ariaLabel: label || undefined, options, selectedIndex })
  return { result: menu.result.then(result => result.kind === 'select' ? { index: result.index } : null), dismiss: menu.dismiss }
}

function readFrameAnchor(value: unknown, frame: HTMLIFrameElement | null): PopupAnchor | null {
  const rect = value as Record<string, unknown> | undefined
  const [x, y, width, height] = [rect?.x, rect?.y, rect?.width, rect?.height].map(Number)
  if (!frame || ![x, y, width, height].every(Number.isFinite)) return null
  const bounds = frame.getBoundingClientRect()
  const left = Math.min(Math.max(bounds.left + x, bounds.left), bounds.right)
  const top = Math.min(Math.max(bounds.top + y, bounds.top), bounds.bottom)
  return { x: left, y: top, width: Math.max(0, Math.min(width, bounds.right - left)), height: Math.max(0, Math.min(height, bounds.bottom - top)) }
}

function readSelectOptions(value: unknown): SelectMenuRequestOptions['options'] | null {
  if (!Array.isArray(value) || value.length === 0 || value.length > MAX_SELECT_OPTIONS) return null
  const options: Array<SelectMenuRequestOptions['options'][number]> = []
  for (const item of value as Array<Record<string, unknown> | null>) {
    if (typeof item?.label !== 'string') return null
    options.push({
      label: item.label.trim().slice(0, MAX_SELECT_TEXT_LENGTH),
      disabled: item.disabled === true,
      ...(typeof item.group === 'string' ? { group: item.group.trim().slice(0, MAX_SELECT_TEXT_LENGTH) } : {})
    })
  }
  return options
}

export async function copyVisualizationImage(getFrame: () => HTMLIFrameElement | null, t: ReturnType<typeof useT>): Promise<void> {
  try {
    await nextPaint()
    await nextPaint()
    const frame = getFrame()
    if (!frame) throw new Error('Visualization frame is unavailable.')
    const rect = frame.getBoundingClientRect()
    await window.api.visualization.copyImage({ x: rect.x, y: rect.y, width: rect.width, height: rect.height })
    addToast(t('toast.copied'), 'success', 2000)
  } catch {
    addToast(t('visualization.copyFailed'), 'error', 3000)
  }
}

function nextPaint(): Promise<void> {
  return new Promise(resolve => requestAnimationFrame(() => resolve()))
}
