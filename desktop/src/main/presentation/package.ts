import { strFromU8, unzipSync } from 'fflate'
import { PRESENTATION_MAX_IMAGE_BYTES } from '../../shared/viewer/presentation'
import { children, parseXml, type XmlNode } from './xml'

const MAX_PART_BYTES = 32 * 1024 * 1024
const MAX_UNZIPPED_BYTES = 384 * 1024 * 1024

const IMAGE_TYPES: Record<string, string> = {
  png: 'image/png',
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  gif: 'image/gif',
  svg: 'image/svg+xml'
}

export interface Relationship {
  type: string
  target: string
  external: boolean
}

export type Relationships = Map<string, Relationship>

export function imageType(partPath: string): string | undefined {
  return IMAGE_TYPES[partPath.slice(partPath.lastIndexOf('.') + 1).toLowerCase()]
}

function isWanted(name: string): boolean {
  const lower = name.toLowerCase()
  return lower.endsWith('.xml') || lower.endsWith('.rels') || imageType(lower) !== undefined
}

function directory(partPath: string): string {
  const slash = partPath.lastIndexOf('/')
  return slash >= 0 ? partPath.slice(0, slash) : ''
}

function resolvePart(base: string, target: string): string {
  const segments = target.startsWith('/') ? [] : directory(base).split('/').filter(Boolean)
  for (const segment of target.split('/')) {
    if (segment === '' || segment === '.') continue
    if (segment === '..') segments.pop()
    else segments.push(segment)
  }
  return segments.join('/')
}

function relsPath(partPath: string): string {
  const slash = partPath.lastIndexOf('/')
  return `${partPath.slice(0, slash + 1)}_rels/${partPath.slice(slash + 1)}.rels`
}

export class PresentationPackage {
  private readonly files: Record<string, Uint8Array>
  private readonly xml = new Map<string, XmlNode | null>()
  private readonly rels = new Map<string, Relationships>()

  constructor(buffer: Uint8Array) {
    let total = 0
    this.files = unzipSync(buffer, {
      filter: (file) => {
        if (!isWanted(file.name)) return false
        const limit = imageType(file.name) ? PRESENTATION_MAX_IMAGE_BYTES : MAX_PART_BYTES
        if (file.originalSize > limit || total + file.originalSize > MAX_UNZIPPED_BYTES) return false
        total += file.originalSize
        return true
      }
    })
  }

  bytes(partPath: string): Uint8Array | undefined {
    return this.files[partPath]
  }

  part(partPath: string): XmlNode | null {
    if (this.xml.has(partPath)) return this.xml.get(partPath)!
    const data = this.files[partPath]
    const parsed = data ? parseXml(strFromU8(data)) : null
    this.xml.set(partPath, parsed)
    return parsed
  }

  root(partPath: string): XmlNode | undefined {
    return this.part(partPath)?.children[0]
  }

  relationships(partPath: string): Relationships {
    const cached = this.rels.get(partPath)
    if (cached) return cached
    const map: Relationships = new Map()
    const document = this.root(relsPath(partPath))
    for (const rel of children(document, 'Relationship')) {
      const id = rel.attrs.Id
      const target = rel.attrs.Target
      if (!id || target === undefined) continue
      const external = rel.attrs.TargetMode === 'External'
      map.set(id, { type: rel.attrs.Type ?? '', target: external ? target : resolvePart(partPath, target), external })
    }
    this.rels.set(partPath, map)
    return map
  }

  related(partPath: string, typeSuffix: string): string | undefined {
    for (const rel of this.relationships(partPath).values()) {
      if (!rel.external && rel.type.endsWith(typeSuffix)) return rel.target
    }
    return undefined
  }
}
