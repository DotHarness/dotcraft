import { transform } from 'sucrase'

export const GENERATIVE_UI_ELEMENT_FACTORY = '__dotcraftElement'
export const GENERATIVE_UI_FRAGMENT = '__dotcraftFragment'

export type GenerativeUiCompileResult =
  | { ok: true; code: string }
  | { ok: false; message: string; line?: number; column?: number }

export function compileGenerativeUiProgram(source: string): GenerativeUiCompileResult {
  try {
    const { code } = transform(source, {
      transforms: ['jsx'],
      jsxRuntime: 'classic',
      jsxPragma: GENERATIVE_UI_ELEMENT_FACTORY,
      jsxFragmentPragma: GENERATIVE_UI_FRAGMENT,
      production: true
    })
    // An undeclared capitalized tag reaches the host as an unknown component instead of throwing.
    const unknownTags = new RegExp(`\\b${GENERATIVE_UI_ELEMENT_FACTORY}\\(([A-Z][A-Za-z0-9_$]*),`, 'g')
    return { ok: true, code: code.replace(unknownTags, (_, name: string) => `${GENERATIVE_UI_ELEMENT_FACTORY}(typeof ${name} === "undefined" ? "${name}" : ${name},`) }
  } catch (error) {
    const failure = error as { message?: unknown; loc?: { line?: unknown; column?: unknown } }
    const message = typeof failure.message === 'string' ? failure.message.replace(/\s*\(\d+:\d+\)$/, '') : String(error)
    const line = typeof failure.loc?.line === 'number' ? failure.loc.line : undefined
    const column = typeof failure.loc?.column === 'number' ? failure.loc.column : undefined
    return { ok: false, message, line, column }
  }
}
