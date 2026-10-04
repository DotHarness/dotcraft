type FsOutcome = { result?: unknown; error?: { code: number; message: string; data?: unknown } }

const FILE_SYSTEM_ERROR = -32103

function normalize(path: string): string {
  return path.replace(/\\/g, '/').replace(/\/+$/, '')
}

function parent(path: string): string {
  return path.slice(0, path.lastIndexOf('/'))
}

function isAbsolute(path: string): boolean {
  return /^([A-Za-z]:)?\//.test(path)
}

export class FakeFiles {
  readonly directories = new Set<string>()
  readonly files = new Map<string, string>()
  readonly blockedNames = new Set<string>()
  readonly tooLarge = new Set<string>()

  constructor(roots: string[], seeded: Record<string, string> = {}) {
    for (const root of roots) this.addDirectory(normalize(root))
    for (const [path, dataBase64] of Object.entries(seeded)) {
      this.addDirectory(parent(normalize(path)))
      this.files.set(normalize(path), dataBase64)
    }
  }

  private addDirectory(path: string): void {
    for (let current = path; current && !this.directories.has(current); current = parent(current)) this.directories.add(current)
  }

  private failure(code: string, path: string): FsOutcome {
    return { error: { code: FILE_SYSTEM_ERROR, message: code, data: { code, params: { path } } } }
  }

  handle(method: string, params: Record<string, unknown>): FsOutcome | null {
    if (!method.startsWith('fs/')) return null
    const raw = typeof params.path === 'string' ? params.path : ''
    if (!isAbsolute(normalize(raw))) return { error: { code: -32602, message: 'path must be absolute' } }
    const path = normalize(raw)
    switch (method) {
      case 'fs/readFile': {
        if (this.directories.has(path)) return this.failure('NotAFile', raw)
        const data = this.files.get(path)
        if (data === undefined) return this.failure('FileNotFound', raw)
        if (this.tooLarge.has(path)) return this.failure('FileTooLarge', raw)
        return { result: { dataBase64: data } }
      }
      case 'fs/createDirectory':
        if (this.files.has(path)) return this.failure('NotADirectory', raw)
        this.addDirectory(path)
        return { result: {} }
      case 'fs/writeFile':
        if (this.blockedNames.has(path.slice(path.lastIndexOf('/') + 1))) return this.failure('PathBlocked', raw)
        if (!this.directories.has(parent(path))) return this.failure('DirectoryNotFound', raw)
        this.files.set(path, String(params.dataBase64 ?? ''))
        return { result: {} }
      default:
        return { error: { code: -32601, message: `Method not found: ${method}` } }
    }
  }
}
