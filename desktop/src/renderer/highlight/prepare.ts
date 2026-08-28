/**
 * Grammars resolve on the renderer thread and the registrations are posted to
 * the worker; a catalogue reachable from both bundles would be emitted twice.
 */
import { isPlainLanguage, languageFromPath, loadLanguage, resolveLanguage } from './languages'
import type { DiffHighlightRequest, FileHighlightRequest } from './types'
import type { LanguageRegistration } from 'shiki/core'

export interface Grammar {
  id: string
  registrations: LanguageRegistration[]
}

export interface PreparedFile {
  lang: string | undefined
  grammars: Grammar[]
}

export interface PreparedDiff {
  deletionLang: string | undefined
  additionLang: string | undefined
  grammars: Grammar[]
}

const imported = new Map<string, Promise<Grammar | undefined>>()

export async function grammarFor(lang: string | undefined): Promise<Grammar | undefined> {
  if (lang === undefined || isPlainLanguage(lang)) return undefined
  let pending = imported.get(lang)
  if (pending === undefined) {
    pending = loadLanguage(lang).then((registrations) =>
      registrations === undefined ? undefined : { id: lang, registrations })
    imported.set(lang, pending)
  }
  return pending
}

function langFor(lang: string | undefined, name: string): string | undefined {
  const resolved = resolveLanguage(lang) ?? resolveLanguage(languageFromPath(name))
  return resolved === undefined || isPlainLanguage(resolved) ? undefined : resolved
}

export async function prepareFile(request: FileHighlightRequest): Promise<PreparedFile> {
  const lang = langFor(request.lang, request.name)
  const grammar = await grammarFor(lang)
  return {
    lang: grammar === undefined ? undefined : lang,
    grammars: grammar === undefined ? [] : [grammar]
  }
}

/**
 * Each side derives its grammar from its own path, so a rename that changes the
 * extension colors each half the way it was actually written.
 */
export async function prepareDiff(request: DiffHighlightRequest): Promise<PreparedDiff> {
  const deletionId = langFor(request.lang, request.prevName ?? request.name)
  const additionId = langFor(request.lang, request.name)
  const [deletion, addition] = await Promise.all([
    grammarFor(deletionId),
    grammarFor(additionId)
  ])

  const grammars: Grammar[] = []
  if (deletion !== undefined) grammars.push(deletion)
  if (addition !== undefined && addition.id !== deletion?.id) grammars.push(addition)

  return {
    deletionLang: deletion === undefined ? undefined : deletionId,
    additionLang: addition === undefined ? undefined : additionId,
    grammars
  }
}

export async function bootGrammars(langs: readonly string[]): Promise<Grammar[]> {
  const loaded = await Promise.all(langs.map((lang) => grammarFor(lang)))
  return loaded.filter((grammar): grammar is Grammar => grammar !== undefined)
}
