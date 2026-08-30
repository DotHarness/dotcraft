// Custom images stay in IndexedDB because plugin settings are for small JSON values.
const DB_NAME = 'dotcraft-wallpaper'
const STORE = 'images'
const VERSION = 1

export interface StoredImage {
  readonly id: string
  readonly name: string
  readonly type: string
  readonly blob: Blob
}

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, VERSION)
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(STORE)) {
        request.result.createObjectStore(STORE, { keyPath: 'id' })
      }
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('Wallpaper store unavailable.'))
  })
}

function run<T>(mode: IDBTransactionMode, work: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  return open().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const request = work(db.transaction(STORE, mode).objectStore(STORE))
        request.onsuccess = () => resolve(request.result)
        request.onerror = () => reject(request.error ?? new Error('Wallpaper store request failed.'))
      })
  )
}

export async function putImage(file: File): Promise<StoredImage> {
  const id = `img-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`
  const record: StoredImage = { id, name: file.name, type: file.type, blob: file }
  await run('readwrite', (store) => store.put(record))
  bump()
  return record
}

export async function listImages(): Promise<readonly StoredImage[]> {
  try {
    return await run<StoredImage[]>('readonly', (store) => store.getAll() as IDBRequest<StoredImage[]>)
  } catch {
    return []
  }
}

export async function deleteImage(id: string): Promise<void> {
  await run('readwrite', (store) => store.delete(id) as unknown as IDBRequest<undefined>)
  const url = objectUrls.get(id)
  if (url !== undefined) {
    URL.revokeObjectURL(url)
    objectUrls.delete(id)
  }
  bump()
}

let revision = 0
const revisionListeners = new Set<() => void>()

export function imagesRevision(): number {
  return revision
}

export function subscribeImages(listener: () => void): () => void {
  revisionListeners.add(listener)
  return () => {
    revisionListeners.delete(listener)
  }
}

function bump(): void {
  revision += 1
  for (const listener of revisionListeners) listener()
}

const objectUrls = new Map<string, string>()

export function urlForImage(image: StoredImage): string {
  const existing = objectUrls.get(image.id)
  if (existing !== undefined) return existing
  const url = URL.createObjectURL(image.blob)
  objectUrls.set(image.id, url)
  return url
}

export function releaseObjectUrls(): void {
  for (const url of objectUrls.values()) URL.revokeObjectURL(url)
  objectUrls.clear()
}
