import { createHighlighterCoreSync, type HighlighterCore } from 'shiki/core'
import { createJavaScriptRegexEngine } from 'shiki/engine/javascript'
import themeDark from '@shikijs/themes/github-dark'
import themeLight from '@shikijs/themes/github-light'
import type { Grammar } from './prepare'

export interface Highlighter {
  core: HighlighterCore
  loaded: Set<string>
}

export function createHighlighter(): Highlighter {
  // JavaScript regex engine, not oniguruma: no WASM asset to ship. Grammars
  // arrive later from the renderer thread; see `prepare.ts`.
  return {
    core: createHighlighterCoreSync({
      themes: [themeLight, themeDark],
      langs: [],
      engine: createJavaScriptRegexEngine({ forgiving: true })
    }),
    loaded: new Set()
  }
}

export function installGrammars(highlighter: Highlighter, grammars: readonly Grammar[]): void {
  for (const grammar of grammars) {
    if (highlighter.loaded.has(grammar.id)) continue
    highlighter.core.loadLanguageSync(grammar.registrations)
    highlighter.loaded.add(grammar.id)
  }
}
