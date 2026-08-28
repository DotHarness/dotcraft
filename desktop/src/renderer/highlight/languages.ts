import { bundledLanguagesInfo } from 'shiki/langs'
import type { LanguageRegistration } from 'shiki/core'

const PLAIN_LANGUAGES = new Set(['text', 'plaintext', 'txt', 'plain', 'ansi'])

export const PLAIN_LANGUAGE = 'text'

/** Preloaded so common languages never flash plain; C# because this is a .NET repository. */
export const BOOT_LANGUAGES = [
  'typescript',
  'javascript',
  'json',
  'shellscript',
  'csharp'
] as const

const EXTENSION_TO_LANGUAGE: Record<string, string> = {
  '.ts': 'typescript',
  '.mts': 'typescript',
  '.cts': 'typescript',
  '.tsx': 'tsx',
  '.js': 'javascript',
  '.mjs': 'javascript',
  '.cjs': 'javascript',
  '.jsx': 'jsx',
  '.json': 'json',
  '.jsonc': 'jsonc',
  '.json5': 'json5',
  '.md': 'markdown',
  '.markdown': 'markdown',
  '.mdx': 'mdx',
  '.css': 'css',
  '.scss': 'scss',
  '.less': 'less',
  '.html': 'html',
  '.htm': 'html',
  '.xhtml': 'html',
  '.vue': 'vue',
  '.svelte': 'svelte',
  '.xml': 'xml',
  '.svg': 'xml',
  '.csproj': 'xml',
  '.props': 'xml',
  '.targets': 'xml',
  '.yaml': 'yaml',
  '.yml': 'yaml',
  '.toml': 'toml',
  '.ini': 'ini',
  '.cfg': 'ini',
  '.conf': 'ini',
  '.env': 'ini',
  '.py': 'python',
  '.pyi': 'python',
  '.rs': 'rust',
  '.go': 'go',
  '.java': 'java',
  '.kt': 'kotlin',
  '.kts': 'kotlin',
  '.c': 'c',
  '.h': 'c',
  '.cpp': 'cpp',
  '.cc': 'cpp',
  '.cxx': 'cpp',
  '.hpp': 'cpp',
  '.hxx': 'cpp',
  '.cs': 'csharp',
  '.vb': 'vb',
  '.fs': 'fsharp',
  '.fsx': 'fsharp',
  '.rb': 'ruby',
  '.php': 'php',
  '.swift': 'swift',
  '.dart': 'dart',
  '.sh': 'shellscript',
  '.bash': 'shellscript',
  '.zsh': 'shellscript',
  '.fish': 'fish',
  '.ps1': 'powershell',
  '.psm1': 'powershell',
  '.psd1': 'powershell',
  '.bat': 'bat',
  '.cmd': 'bat',
  '.sql': 'sql',
  '.graphql': 'graphql',
  '.gql': 'graphql',
  '.proto': 'proto',
  '.lua': 'lua',
  '.r': 'r',
  '.tf': 'terraform',
  '.tfvars': 'terraform',
  '.hcl': 'hcl',
  '.dockerfile': 'docker',
  '.patch': 'diff',
  '.diff': 'diff',
  '.txt': PLAIN_LANGUAGE,
  '.log': PLAIN_LANGUAGE
}

const FILENAME_TO_LANGUAGE: Record<string, string> = {
  dockerfile: 'docker',
  makefile: 'make',
  gnumakefile: 'make',
  '.gitattributes': 'ini',
  '.gitignore': 'ini',
  '.npmrc': 'ini',
  '.editorconfig': 'ini'
}

export function languageFromPath(filePath: string): string {
  const normalized = filePath.replace(/\\/g, '/')
  const lastSlash = normalized.lastIndexOf('/')
  const fileName = normalized.slice(lastSlash + 1).toLowerCase()

  const byName = FILENAME_TO_LANGUAGE[fileName]
  if (byName !== undefined) return byName

  const lastDot = fileName.lastIndexOf('.')
  // A leading dot is part of the name (`.gitignore`), not an extension.
  if (lastDot <= 0) return PLAIN_LANGUAGE
  return EXTENSION_TO_LANGUAGE[fileName.slice(lastDot)] ?? PLAIN_LANGUAGE
}

export function isPlainLanguage(lang: string | undefined): boolean {
  return lang === undefined || PLAIN_LANGUAGES.has(lang)
}

// A Map, not an object: fence labels are model-authored, so a label like
// `constructor` must miss rather than resolve an inherited property.
const CANONICAL_LANGUAGE = new Map<string, string>(
  bundledLanguagesInfo.flatMap((info) => [
    [info.id, info.id] as const,
    ...(info.aliases ?? []).map((alias) => [alias, info.id] as const)
  ])
)

/** Each loader is its own dynamic import, so a grammar's module ships only when rendered. */
const LANGUAGE_LOADERS = new Map<string, () => Promise<{ default: LanguageRegistration[] }>>(
  bundledLanguagesInfo.map((info) => [info.id, info.import])
)

export function resolveLanguage(lang: string | undefined): string | undefined {
  if (lang === undefined) return undefined
  const key = lang.trim().toLowerCase()
  if (key.length === 0) return undefined
  if (PLAIN_LANGUAGES.has(key)) return PLAIN_LANGUAGE
  return CANONICAL_LANGUAGE.get(key)
}

export async function loadLanguage(lang: string): Promise<LanguageRegistration[] | undefined> {
  const load = LANGUAGE_LOADERS.get(lang)
  if (load === undefined) return undefined
  return (await load()).default
}
