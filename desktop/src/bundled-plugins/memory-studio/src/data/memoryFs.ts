import { errorCode, type Rpc } from './rpc'

/** File access for one store directory and its files, through the AppServer file system methods. */
export interface MemoryFs {
  /** `null` when the file does not exist. */
  readText(path: string): Promise<string | null>
  writeText(path: string, text: string): Promise<void>
  remove(path: string): Promise<void>
  /** Top-level file names, or `null` when the directory does not exist. */
  listFiles(directory: string): Promise<string[] | null>
  modifiedAt(path: string): Promise<number>
  ensureDirectory(directory: string): Promise<void>
}

const MISSING = new Set(['FileNotFound', 'DirectoryNotFound'])

export function decodeUtf8(base64: string): string {
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return new TextDecoder().decode(bytes)
}

export function encodeUtf8(text: string): string {
  const bytes = new TextEncoder().encode(text)
  let binary = ''
  for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  return btoa(binary)
}

export function appServerFs(rpc: Rpc): MemoryFs {
  return {
    async readText(path) {
      try {
        const { dataBase64 } = await rpc.request('fs/readFile', { path })
        return decodeUtf8(dataBase64)
      } catch (error) {
        if (MISSING.has(errorCode(error) ?? '')) return null
        throw error
      }
    },
    async writeText(path, text) {
      await rpc.request('fs/writeFile', { path, dataBase64: encodeUtf8(text) })
    },
    async remove(path) {
      await rpc.request('fs/remove', { path, recursive: false, force: true })
    },
    async listFiles(directory) {
      try {
        const { entries } = await rpc.request('fs/readDirectory', { path: directory })
        return entries.filter((entry) => entry.isFile).map((entry) => entry.fileName)
      } catch (error) {
        if (MISSING.has(errorCode(error) ?? '')) return null
        throw error
      }
    },
    async modifiedAt(path) {
      const metadata = await rpc.request('fs/getMetadata', { path })
      return metadata.modifiedAtMs
    },
    async ensureDirectory(directory) {
      await rpc.request('fs/createDirectory', { path: directory, recursive: true })
    }
  }
}
