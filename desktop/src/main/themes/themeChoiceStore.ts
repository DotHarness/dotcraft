import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises'
import { dirname } from 'node:path'
import { randomUUID } from 'node:crypto'
import {
  DEFAULT_THEME_CHOICES, THEME_MAX_CHAT_CHOICES,
  patchThemeChoices, validateThemeChoice, validateThemeScope,
  type ThemeAppearance, type ThemeChoices, type ThemeSlots, type ThemeSlotsPatch
} from '../../shared/themes'

type ChatSlots = ThemeSlots & { at: number }
interface MutableChoices {
  everywhere: ThemeSlots
  chats: Record<string, ChatSlots>
}

const APPEARANCES: readonly ThemeAppearance[] = ['light', 'dark']

export class ThemeChoiceStore {
  private pending: Promise<unknown> = Promise.resolve()
  private readonly listeners = new Set<(choices: ThemeChoices) => void>()

  constructor(private readonly filePath: string, private readonly now: () => number = Date.now) {}

  onChanged(listener: (choices: ThemeChoices) => void): () => void {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  async get(): Promise<ThemeChoices> {
    await this.pending
    return this.read()
  }

  set(scopeValue: unknown, patchValue: unknown): Promise<ThemeChoices> {
    return this.serialize(async () => {
      const scope = validateThemeScope(scopeValue)
      const source = (patchValue ?? {}) as Record<string, unknown>
      const patch: ThemeSlotsPatch = {}
      for (const appearance of APPEARANCES) {
        if (appearance in source) patch[appearance] = source[appearance] === null ? null : validateThemeChoice(source[appearance])
      }
      const choices = patchThemeChoices(await this.read(), scope, patch, this.now()) as MutableChoices
      prune(choices)
      await this.write(choices)
      return choices
    })
  }

  forget(id: string): Promise<ThemeChoices> {
    return this.serialize(async () => {
      const choices = await this.read()
      let changed = false
      const strip = (slots: ThemeSlots): boolean => {
        for (const appearance of APPEARANCES) {
          const choice = slots[appearance]
          if (choice?.kind !== 'theme' || choice.id !== id) continue
          delete slots[appearance]
          changed = true
        }
        return !slots.light && !slots.dark
      }
      strip(choices.everywhere)
      for (const [key, slots] of Object.entries(choices.chats)) if (strip(slots)) delete choices.chats[key]
      if (changed) await this.write(choices)
      return choices
    })
  }

  private serialize<T>(work: () => Promise<T>): Promise<T> {
    const next = this.pending.then(work)
    this.pending = next.catch(() => {})
    return next
  }

  private async read(): Promise<MutableChoices> {
    let text: string
    try { text = await readFile(this.filePath, 'utf8') }
    catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') return structuredClone(DEFAULT_THEME_CHOICES) as MutableChoices
      throw error
    }
    const value = JSON.parse(text) as Record<string, unknown>
    const choices: MutableChoices = { everywhere: readSlots(value.everywhere), chats: {} }
    for (const [key, slots] of Object.entries(asRecord(value.chats))) {
      const at = (slots as { at?: unknown } | null)?.at
      choices.chats[key] = { ...readSlots(slots), at: typeof at === 'number' && Number.isFinite(at) ? at : 0 }
    }
    return choices
  }

  private async write(choices: MutableChoices): Promise<void> {
    await mkdir(dirname(this.filePath), { recursive: true })
    const temporary = `${this.filePath}.${randomUUID()}.tmp`
    try {
      await writeFile(temporary, JSON.stringify(choices), { encoding: 'utf8', flag: 'wx' })
      await rename(temporary, this.filePath)
    } finally {
      await rm(temporary, { force: true })
    }
    for (const listener of this.listeners) listener(choices)
  }
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}
}

function readSlots(value: unknown): ThemeSlots {
  const source = asRecord(value)
  const slots: ThemeSlots = {}
  for (const appearance of APPEARANCES) {
    if (source[appearance] !== undefined) slots[appearance] = validateThemeChoice(source[appearance])
  }
  return slots
}

function prune(choices: MutableChoices): void {
  const entries = Object.entries(choices.chats)
  if (entries.length <= THEME_MAX_CHAT_CHOICES) return
  entries.sort((a, b) => b[1].at - a[1].at)
  for (const [key] of entries.slice(THEME_MAX_CHAT_CHOICES)) delete choices.chats[key]
}
