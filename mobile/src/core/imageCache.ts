const LIMIT = 24
const images = new Map<string, string>()

export function imageKey(scope: string, path: string): string {
  return `${scope}\n${path}`
}

export function imageScope(computerId: string, projectId: string): string {
  return `${computerId}\n${projectId}`
}

export function rememberImage(key: string, uri: string): void {
  images.delete(key)
  images.set(key, uri)
  if (images.size > LIMIT) images.delete(images.keys().next().value!)
}

export function rememberedImage(key: string): string | undefined {
  return images.get(key)
}

export function forgetImages(computerId: string): void {
  for (const key of [...images.keys()]) if (key.startsWith(`${computerId}\n`)) images.delete(key)
}
