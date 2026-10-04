import { Platform, ToastAndroid } from 'react-native'
import Downloads from '../../modules/downloads'

export async function saveToDownloads(dataBase64: string, fileName: string, mimeType: string): Promise<string> {
  return await Downloads.save(dataBase64, fileName, mimeType)
}

export function isUnsupportedSave(error: unknown): boolean {
  return (error as { code?: unknown } | null)?.code === 'UNSUPPORTED'
}

export function showToast(text: string): void {
  if (Platform.OS === 'android') ToastAndroid.show(text, ToastAndroid.SHORT)
}
