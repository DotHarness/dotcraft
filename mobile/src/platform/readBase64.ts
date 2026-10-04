import { File } from 'expo-file-system'

export function readBase64(uri: string): Promise<string> {
  return new File(uri).base64()
}
