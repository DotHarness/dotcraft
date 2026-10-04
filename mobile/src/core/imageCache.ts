const LIMIT = 24
const images = new Map<string, string>()

export function imageKey(scope: string, path: string): string {
  return `${scope}\n${path}`
}

export function imageScope(fingerprint: string, projectId: string): string {
  return `${fingerprint}\n${projectId}`
}

export function rememberImage(key: string, uri: string): void {
  images.delete(key)
  images.set(key, uri)
  if (images.size > LIMIT) images.delete(images.keys().next().value!)
}

export function rememberedImage(key: string): string | undefined {
  return images.get(key)
}

export function forgetImages(): void {
  images.clear()
}
