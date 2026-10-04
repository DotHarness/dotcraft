import * as DocumentPicker from 'expo-document-picker'
import { ImageManipulator, SaveFormat } from 'expo-image-manipulator'
import * as ImagePicker from 'expo-image-picker'
import { base64Bytes, fitWithin, MAX_FILE_BYTES } from '../core/attachments'
import type { FileAttachment, PhotoAttachment } from '../core/draft'
import { readBase64 } from './readBase64'

let counter = 0

function localId(): string {
  counter += 1
  return `attachment-${Date.now().toString(36)}-${counter}`
}

async function encodePhoto(uri: string): Promise<PhotoAttachment> {
  const original = await ImageManipulator.manipulate(uri).renderAsync()
  const size = fitWithin(original.width, original.height)
  const image = size ? await ImageManipulator.manipulate(original).resize(size).renderAsync() : original
  const saved = await image.saveAsync({ format: SaveFormat.JPEG, compress: 0.85, base64: true })
  return { id: localId(), dataUrl: `data:image/jpeg;base64,${saved.base64 ?? ''}` }
}

export async function pickPhotos(): Promise<PhotoAttachment[]> {
  const result = await ImagePicker.launchImageLibraryAsync({ mediaTypes: ['images'], allowsMultipleSelection: true, quality: 1 })
  if (result.canceled) return []
  return await Promise.all(result.assets.map((asset) => encodePhoto(asset.uri)))
}

export type PickedFile = { kind: 'file'; file: FileAttachment } | { kind: 'tooLarge'; name: string } | null

export async function pickFile(): Promise<PickedFile> {
  const result = await DocumentPicker.getDocumentAsync({ type: '*/*', copyToCacheDirectory: true, multiple: false })
  if (result.canceled || result.assets.length === 0) return null
  const asset = result.assets[0]
  if ((asset.size ?? 0) > MAX_FILE_BYTES) return { kind: 'tooLarge', name: asset.name }
  const dataBase64 = await readBase64(asset.uri)
  if (base64Bytes(dataBase64) > MAX_FILE_BYTES) return { kind: 'tooLarge', name: asset.name }
  return { kind: 'file', file: { id: localId(), name: asset.name, dataBase64 } }
}
