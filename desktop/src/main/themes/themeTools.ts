import type { DynamicToolCallResult, DynamicToolFunctionSpec } from '../desktopRuntimeThreadTools'
import { isRecord, type Theme } from '../../shared/themes'
import type { ThemeStore } from './themeStore'

export interface DesktopThemeHost {
  store: ThemeStore
  preview(themeId: string, threadId: string): void
}

const color = { type: 'string', description: 'Hex, rgb() or hsl() color.' }

export const themeToolDefinition: DynamicToolFunctionSpec = {
  type: 'function', name: 'Theme',
  description: 'Create and edit DotCraft app themes on this device: app colors, a backdrop behind the app, and the tint of the surfaces over it. Use the themes skill for authoring. List or read saved themes, save one, then preview it. Saving never applies a theme: the user applies it by selecting it. A preview is shown only in the calling foreground chat. Updates require expectedRevision from read.',
  inputSchema: {
    type: 'object', additionalProperties: false,
    properties: {
      action: { type: 'string', enum: ['list', 'read', 'save', 'preview'] },
      id: { type: 'string', description: 'User theme id for read or preview.' },
      theme: {
        type: 'object', additionalProperties: false,
        properties: {
          id: { type: 'string', description: 'Stable lowercase theme id. Keep the same id when editing.' },
          name: { type: 'string' },
          colorScheme: { type: 'string', enum: ['light', 'dark'], description: 'The appearance this theme is made for.' },
          colors: {
            type: 'object', additionalProperties: false,
            description: 'Six-digit hex seeds the app palette is derived from. ink must reach 4.5:1 against surface.',
            properties: { surface: { type: 'string' }, ink: { type: 'string' }, accent: { type: 'string' }, contrast: { type: 'number', minimum: 0, maximum: 100 } },
            required: ['surface', 'ink', 'accent', 'contrast']
          },
          backdrop: {
            type: 'object', additionalProperties: false,
            properties: {
              kind: { type: 'string', enum: ['scene', 'gradient', 'color', 'image'], description: 'image keeps the picture of an existing image theme.' },
              format: { type: 'string', enum: ['svg', 'html'], description: 'Scene only.' },
              source: { type: 'string', description: 'Scene only. Self-contained source, at most 1 MiB. SVG starts with <svg>.' },
              poster: { type: 'string', description: 'Scene only. Composed static SVG used for reduced motion, thumbnails and runtime failure, at most 256 KiB.' },
              stops: { type: 'array', minItems: 2, maxItems: 16, items: color, description: 'Gradient only.' },
              angle: { type: 'number', minimum: -360, maximum: 360, description: 'Gradient only. Degrees as in CSS, default 180.' },
              shape: { type: 'string', enum: ['linear', 'radial'], description: 'Gradient only.' },
              color: { ...color, description: 'Color only.' },
              fit: { type: 'string', enum: ['cover', 'contain', 'tile'], description: 'Image only.' }
            },
            required: ['kind']
          },
          tint: { ...color, description: 'Color of the app surfaces over the backdrop. Backdrop only.' },
          pair: { type: 'string', description: 'Id of the same theme made for the other appearance.' },
          expectedRevision: { type: 'integer', minimum: 1 }
        },
        required: ['id', 'name', 'colorScheme']
      }
    }, required: ['action']
  }
}

function summary(theme: Theme): Record<string, unknown> {
  const { backdrop, ...rest } = theme
  return { ...rest, ...(backdrop ? { backdrop: backdrop.kind } : {}) }
}

export async function themeTool(host: DesktopThemeHost | undefined, args: Record<string, unknown>, threadId?: string): Promise<DynamicToolCallResult> {
  if (!host) return failure('ThemeUnavailable', 'Theme authoring requires a connected DotCraft Desktop window.')
  try {
    if (args.action === 'list') return result({ themes: (await host.store.list()).map(summary) })
    if (args.action === 'save') {
      if (!threadId) return failure('InvalidParams', 'Saving a theme requires an authoring thread.')
      return result({ theme: summary(await host.store.save(await keepImage(host.store, args.theme), threadId)), applied: false })
    }
    if (args.action !== 'read' && args.action !== 'preview') return failure('InvalidParams', 'action must be list, read, save or preview.')
    if (typeof args.id !== 'string' || !args.id) return failure('InvalidParams', 'Theme id is required.')
    const theme = (await host.store.list()).find((theme) => theme.id === args.id)
    if (!theme) return failure('ThemeNotFound', `Theme '${args.id}' was not found on this device.`)
    if (args.action === 'read') {
      return result({ theme: theme.backdrop?.kind === 'image' ? { ...theme, backdrop: { kind: 'image', fit: theme.backdrop.fit } } : theme })
    }
    if (!threadId) return failure('InvalidParams', 'Preview requires the calling thread.')
    host.preview(theme.id, threadId)
    return result({ previewRequested: true, applied: false, themeId: theme.id, message: 'The foreground authoring chat previews this theme. The user applies it by selecting it in the picker.' })
  } catch (error) {
    return failure('ThemeError', error instanceof Error ? error.message : String(error))
  }
}

async function keepImage(store: ThemeStore, value: unknown): Promise<unknown> {
  if (!isRecord(value) || !isRecord(value.backdrop) || value.backdrop.kind !== 'image' || value.backdrop.image !== undefined) return value
  const previous = (await store.list()).find((theme) => theme.id === value.id)?.backdrop
  return previous?.kind === 'image' ? { ...value, backdrop: { ...value.backdrop, image: previous.image } } : value
}

function result(value: unknown): DynamicToolCallResult {
  return { success: true, structuredContent: value, contentItems: [{ type: 'text', text: JSON.stringify(value) }] }
}

function failure(errorCode: string, errorMessage: string): DynamicToolCallResult {
  return { success: false, errorCode, errorMessage }
}
