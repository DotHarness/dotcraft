/**
 * cyrb53, not cryptographic. The keys below also carry the path and the length,
 * so a collision alone cannot put one file's colors on another.
 */
export function contentHash(text: string): string {
  let h1 = 0xdeadbeef
  let h2 = 0x41c6ce57
  for (let index = 0; index < text.length; index++) {
    const ch = text.charCodeAt(index)
    h1 = Math.imul(h1 ^ ch, 2654435761)
    h2 = Math.imul(h2 ^ ch, 1597334677)
  }
  h1 = Math.imul(h1 ^ (h1 >>> 16), 2246822507) ^ Math.imul(h2 ^ (h2 >>> 13), 3266489909)
  h2 = Math.imul(h2 ^ (h2 >>> 16), 2246822507) ^ Math.imul(h1 ^ (h1 >>> 13), 3266489909)
  return (4294967296 * (2097151 & h2) + (h1 >>> 0)).toString(16)
}

export function fileCacheKey(name: string, lang: string | undefined, contents: string): string {
  return `${name} ${lang ?? ''} ${contents.length} ${contentHash(contents)}`
}

export function diffCacheKey(
  name: string,
  prevName: string | undefined,
  lang: string | undefined,
  sides: string[]
): string {
  const joined = sides.join('')
  return `${name} ${prevName ?? ''} ${lang ?? ''} ${joined.length} ${contentHash(joined)}`
}
