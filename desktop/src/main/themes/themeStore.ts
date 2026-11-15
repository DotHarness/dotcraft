import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises'
import { dirname } from 'node:path'
import { randomUUID } from 'node:crypto'
import { validateTheme, THEME_MAX_LIBRARY_BYTES, THEME_MAX_THEMES, type Theme } from '../../shared/themes'

export class ThemeStore {
  private pending: Promise<unknown> = Promise.resolve()
  private readonly listeners = new Set<() => void>()

  constructor(private readonly filePath: string) {}

  onChanged(listener: () => void): () => void {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  async list(): Promise<Theme[]> {
    await this.pending
    return this.read()
  }

  save(value: unknown, authorThreadId?: string): Promise<Theme> {
    return this.serialize(async () => {
      const input = validateTheme(value)
      const themes = await this.read()
      const previous = themes.find((theme) => theme.id === input.id)
      if (previous ? previous.revision !== input.expectedRevision : input.expectedRevision !== undefined) {
        throw new Error(`Theme revision conflict. Read ${input.id} again before saving.`)
      }
      if (!previous && themes.length >= THEME_MAX_THEMES) throw new Error(`The theme library holds at most ${THEME_MAX_THEMES} themes.`)
      const { expectedRevision: _revision, ...content } = input
      const author = authorThreadId ?? previous?.authorThreadId
      const theme: Theme = {
        ...content, revision: (previous?.revision ?? 0) + 1, updatedAt: new Date().toISOString(),
        ...(author ? { authorThreadId: author } : {})
      }
      await this.write([...themes.filter((item) => item.id !== theme.id), theme])
      return theme
    })
  }

  remove(id: string): Promise<void> {
    return this.serialize(async () => {
      if (typeof id !== 'string' || id.startsWith('builtin-')) throw new Error('A user theme id is required.')
      const themes = await this.read()
      if (!themes.some((theme) => theme.id === id)) return
      await this.write(themes.filter((theme) => theme.id !== id))
    })
  }

  private serialize<T>(work: () => Promise<T>): Promise<T> {
    const next = this.pending.then(work)
    this.pending = next.catch(() => {})
    return next
  }

  private async read(): Promise<Theme[]> {
    let text: string
    try { text = await readFile(this.filePath, 'utf8') }
    catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') return []
      throw error
    }
    if (Buffer.byteLength(text) > THEME_MAX_LIBRARY_BYTES) throw new Error('Theme library exceeds its size limit.')
    const entries: unknown = JSON.parse(text)
    if (!Array.isArray(entries) || entries.length > THEME_MAX_THEMES) throw new Error('Invalid theme library.')
    const seen = new Set<string>()
    return entries.map((entry) => {
      const input = validateTheme(entry)
      if (!Number.isSafeInteger(entry.revision) || entry.revision < 1 || seen.has(input.id)) throw new Error('Invalid theme revision or duplicate id in theme library.')
      seen.add(input.id)
      const { expectedRevision: _revision, ...content } = input
      return {
        ...content, revision: entry.revision, updatedAt: typeof entry.updatedAt === 'string' ? entry.updatedAt : '',
        ...(typeof entry.authorThreadId === 'string' ? { authorThreadId: entry.authorThreadId } : {})
      }
    })
  }

  private async write(themes: Theme[]): Promise<void> {
    const text = JSON.stringify(themes)
    if (Buffer.byteLength(text) > THEME_MAX_LIBRARY_BYTES) throw new Error('Theme library exceeds 64 MiB. Remove a theme before saving.')
    await mkdir(dirname(this.filePath), { recursive: true })
    const temporary = `${this.filePath}.${randomUUID()}.tmp`
    try {
      await writeFile(temporary, text, { encoding: 'utf8', flag: 'wx' })
      await rename(temporary, this.filePath)
    } finally {
      await rm(temporary, { force: true })
    }
    for (const listener of this.listeners) listener()
  }
}
