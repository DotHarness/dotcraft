import { decodeBase64, decodeUtf8 } from './utf8'

export type FileContent = { kind: 'text'; text: string } | { kind: 'image'; uri: string } | { kind: 'other' }

export type FileFailure = 'tooLarge' | 'failed'

const IMAGE_TYPES: Record<string, string> = {
  png: 'image/png',
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  gif: 'image/gif',
  webp: 'image/webp',
  bmp: 'image/bmp',
}

function extension(path: string): string {
  const name = path.split(/[\\/]/).pop() ?? ''
  const dot = name.lastIndexOf('.')
  return dot > 0 ? name.slice(dot + 1).toLowerCase() : ''
}

export function mediaTypeOf(path: string): string {
  return IMAGE_TYPES[extension(path)] ?? 'application/octet-stream'
}

export function fileContent(path: string, dataBase64: string): FileContent {
  const mediaType = IMAGE_TYPES[extension(path)]
  if (mediaType) return { kind: 'image', uri: `data:${mediaType};base64,${dataBase64}` }
  const bytes = decodeBase64(dataBase64)
  if (bytes.includes(0)) return { kind: 'other' }
  try {
    return { kind: 'text', text: decodeUtf8(bytes).replace(/^﻿/, '') }
  } catch {
    return { kind: 'other' }
  }
}

export function fileFailure(error: unknown): FileFailure {
  const data = (error as { data?: { code?: unknown } } | null)?.data
  return data?.code === 'FileTooLarge' ? 'tooLarge' : 'failed'
}
