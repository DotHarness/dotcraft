/**
 * Extension → Monaco language identifier.
 * A focused subset covering the most common source files.
 * Unmapped extensions fall back to 'plaintext'.
 */
const EXT_TO_LANG: Record<string, string> = {
  '.ts': 'typescript',
  '.tsx': 'typescript',
  '.js': 'javascript',
  '.jsx': 'javascript',
  '.mjs': 'javascript',
  '.cjs': 'javascript',
  '.json': 'json',
  '.jsonc': 'json',
  '.json5': 'json',
  '.md': 'markdown',
  '.mdx': 'markdown',
  '.css': 'css',
  '.scss': 'scss',
  '.less': 'less',
  '.html': 'html',
  '.htm': 'html',
  '.xml': 'xml',
  '.xhtml': 'html',
  '.yaml': 'yaml',
  '.yml': 'yaml',
  '.toml': 'ini',
  '.ini': 'ini',
  '.cfg': 'ini',
  '.conf': 'ini',
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
  '.rb': 'ruby',
  '.php': 'php',
  '.swift': 'swift',
  '.dart': 'dart',
  '.sh': 'shell',
  '.bash': 'shell',
  '.zsh': 'shell',
  '.fish': 'shell',
  '.ps1': 'powershell',
  '.psm1': 'powershell',
  '.bat': 'bat',
  '.cmd': 'bat',
  '.sql': 'sql',
  '.graphql': 'graphql',
  '.gql': 'graphql',
  '.proto': 'proto',
  '.lua': 'lua',
  '.r': 'r',
  '.tf': 'hcl',
  '.hcl': 'hcl',
  '.txt': 'plaintext',
  '.log': 'plaintext',
  '.svg': 'xml',
  '.vue': 'html',
  '.svelte': 'html',
  '.dockerfile': 'dockerfile',
  '.env': 'ini'
}

/** Returns the Monaco language ID for the given absolute or relative path. */
export function detectLanguage(filePath: string): string {
  const normalized = filePath.replace(/\\/g, '/')
  const lastDot = normalized.lastIndexOf('.')
  const lastSlash = normalized.lastIndexOf('/')
  if (lastDot === -1 || lastDot < lastSlash) return 'plaintext'
  const ext = normalized.slice(lastDot).toLowerCase()
  return EXT_TO_LANG[ext] ?? 'plaintext'
}
