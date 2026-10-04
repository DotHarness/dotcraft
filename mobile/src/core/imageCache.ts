const LIMIT = 24
const images = new Map<string, string>()

export function rememberImage(path: string, uri: string): void {
  images.delete(path)
  images.set(path, uri)
  if (images.size > LIMIT) images.delete(images.keys().next().value!)
}

export function rememberedImage(path: string): string | undefined {
  return images.get(path)
}
