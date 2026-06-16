/**
 * File-type icon mapping backed by the colorful VS Code Icons set (Iconify).
 *
 * `fileIconName()` is synchronous and returns an Iconify icon name
 * (`vscode-icons:...`). The icon collection (~1.3 MB) is registered lazily by
 * `ensureVscodeIcons()` so it stays out of the main renderer chunk; until it is
 * registered, <FileTypeIcon> renders a neutral lucide fallback.
 *
 * Every base name referenced here was verified to exist in
 * `@iconify-json/vscode-icons` — do not add a name without confirming it, or
 * the <Icon> will render blank.
 */
import { addCollection } from '@iconify/react'
import { useSyncExternalStore } from 'react'

const PREFIX = 'vscode-icons'

export const DEFAULT_FILE_ICON = `${PREFIX}:default-file`
export const DEFAULT_FOLDER_ICON = `${PREFIX}:default-folder`
export const DEFAULT_FOLDER_OPEN_ICON = `${PREFIX}:default-folder-opened`

/** Exact filename (lower-cased) → icon base name. Checked before extension. */
const FILENAME_ICONS: Record<string, string> = {
  'package.json': 'file-type-npm',
  'package-lock.json': 'file-type-npm',
  '.npmrc': 'file-type-npm',
  'dockerfile': 'file-type-docker',
  '.dockerignore': 'file-type-docker',
  '.gitignore': 'file-type-git',
  '.gitattributes': 'file-type-git',
  '.gitmodules': 'file-type-git',
  '.editorconfig': 'file-type-editorconfig',
  '.eslintrc': 'file-type-eslint',
  '.eslintrc.js': 'file-type-eslint',
  '.eslintrc.cjs': 'file-type-eslint',
  '.eslintrc.json': 'file-type-eslint',
  '.prettierrc': 'file-type-prettier',
  '.prettierrc.json': 'file-type-prettier',
  'cmakelists.txt': 'file-type-cmake',
  'license': 'file-type-license',
  'license.md': 'file-type-license',
  'license.txt': 'file-type-license',
  'todo': 'file-type-todo',
  'todo.md': 'file-type-todo'
}

/** Extension (without leading dot, lower-cased) → icon base name. */
const EXTENSION_ICONS: Record<string, string> = {
  // TypeScript / JavaScript
  ts: 'file-type-typescript',
  mts: 'file-type-typescript',
  cts: 'file-type-typescript',
  tsx: 'file-type-reactts',
  js: 'file-type-js',
  mjs: 'file-type-js',
  cjs: 'file-type-js',
  jsx: 'file-type-reactjs',
  json: 'file-type-json',
  jsonc: 'file-type-json',
  json5: 'file-type-json',
  vue: 'file-type-vue',
  // .NET / JVM
  cs: 'file-type-csharp',
  csproj: 'file-type-csproj',
  sln: 'file-type-sln',
  fs: 'file-type-fsharp',
  fsx: 'file-type-fsharp',
  vb: 'file-type-vb',
  java: 'file-type-java',
  kt: 'file-type-kotlin',
  kts: 'file-type-kotlin',
  scala: 'file-type-scala',
  clj: 'file-type-clojure',
  cljs: 'file-type-clojure',
  gradle: 'file-type-gradle',
  // systems / native
  c: 'file-type-c',
  h: 'file-type-cheader',
  cpp: 'file-type-cpp',
  cc: 'file-type-cpp',
  cxx: 'file-type-cpp',
  hpp: 'file-type-cppheader',
  hxx: 'file-type-cppheader',
  m: 'file-type-objectivec',
  mm: 'file-type-objectivec',
  rs: 'file-type-rust',
  go: 'file-type-go',
  swift: 'file-type-swift',
  hs: 'file-type-haskell',
  // scripting
  py: 'file-type-python',
  pyi: 'file-type-python',
  pyx: 'file-type-python',
  rb: 'file-type-ruby',
  php: 'file-type-php',
  lua: 'file-type-lua',
  r: 'file-type-r',
  jl: 'file-type-julia',
  pl: 'file-type-perl',
  pm: 'file-type-perl',
  ex: 'file-type-elixir',
  exs: 'file-type-elixir',
  tcl: 'file-type-tcl',
  // shells
  sh: 'file-type-shell',
  bash: 'file-type-shell',
  zsh: 'file-type-shell',
  fish: 'file-type-shell',
  ps1: 'file-type-powershell',
  psm1: 'file-type-powershell',
  bat: 'file-type-bat',
  cmd: 'file-type-bat',
  // markup / styles
  html: 'file-type-html',
  htm: 'file-type-html',
  xhtml: 'file-type-html',
  css: 'file-type-css',
  scss: 'file-type-scss',
  sass: 'file-type-sass',
  less: 'file-type-less',
  styl: 'file-type-stylus',
  hbs: 'file-type-handlebars',
  svg: 'file-type-svg',
  // data / config
  yaml: 'file-type-yaml',
  yml: 'file-type-yaml',
  toml: 'file-type-toml',
  xml: 'file-type-xml',
  ini: 'file-type-ini',
  cfg: 'file-type-ini',
  conf: 'file-type-config',
  config: 'file-type-config',
  sql: 'file-type-sql',
  graphql: 'file-type-graphql',
  gql: 'file-type-graphql',
  proto: 'file-type-protobuf',
  tf: 'file-type-terraform',
  hcl: 'file-type-terraform',
  cmake: 'file-type-cmake',
  // docs / text
  md: 'file-type-markdown',
  mdx: 'file-type-markdown',
  txt: 'file-type-text',
  rst: 'file-type-text',
  adoc: 'file-type-text',
  log: 'file-type-log',
  diff: 'file-type-diff',
  patch: 'file-type-diff',
  // images
  png: 'file-type-image',
  jpg: 'file-type-image',
  jpeg: 'file-type-image',
  gif: 'file-type-image',
  webp: 'file-type-image',
  bmp: 'file-type-image',
  ico: 'file-type-image',
  tiff: 'file-type-image',
  tif: 'file-type-image',
  avif: 'file-type-image',
  // binary-ish
  pdf: 'file-type-pdf2',
  zip: 'file-type-zip',
  tar: 'file-type-zip',
  gz: 'file-type-zip',
  '7z': 'file-type-zip',
  rar: 'file-type-zip',
  xls: 'file-type-excel',
  xlsx: 'file-type-excel',
  doc: 'file-type-word',
  docx: 'file-type-word',
  ttf: 'file-type-font',
  otf: 'file-type-font',
  woff: 'file-type-font',
  woff2: 'file-type-font',
  eot: 'file-type-font',
  mp4: 'file-type-video',
  mov: 'file-type-video',
  avi: 'file-type-video',
  mkv: 'file-type-video',
  webm: 'file-type-video',
  mp3: 'file-type-audio',
  wav: 'file-type-audio',
  flac: 'file-type-audio',
  ogg: 'file-type-audio',
  key: 'file-type-key',
  pem: 'file-type-key',
  crt: 'file-type-cert',
  cert: 'file-type-cert',
  cer: 'file-type-cert'
}

function basename(pathOrName: string): string {
  const normalized = pathOrName.replace(/\\/g, '/')
  const trimmed = normalized.endsWith('/') ? normalized.slice(0, -1) : normalized
  const idx = trimmed.lastIndexOf('/')
  return idx >= 0 ? trimmed.slice(idx + 1) : trimmed
}

/**
 * Resolves the Iconify icon name for a file or folder path.
 * Returns a fully-qualified `vscode-icons:<name>` string.
 */
export function fileIconName(
  pathOrName: string,
  opts: { dir?: boolean; expanded?: boolean } = {}
): string {
  if (opts.dir) {
    return opts.expanded ? DEFAULT_FOLDER_OPEN_ICON : DEFAULT_FOLDER_ICON
  }

  const name = basename(pathOrName).toLowerCase()
  if (!name) return DEFAULT_FILE_ICON

  const byName = FILENAME_ICONS[name]
  if (byName) return `${PREFIX}:${byName}`

  // Compound extensions first (e.g. `.d.ts`).
  if (name.endsWith('.d.ts')) return `${PREFIX}:file-type-typescriptdef`

  const dot = name.lastIndexOf('.')
  if (dot > 0) {
    const ext = name.slice(dot + 1)
    const byExt = EXTENSION_ICONS[ext]
    if (byExt) return `${PREFIX}:${byExt}`
  }

  return DEFAULT_FILE_ICON
}

// ─── Lazy collection registration ─────────────────────────────────────────────

let registered = false
let registerPromise: Promise<void> | null = null
const listeners = new Set<() => void>()

/**
 * Lazily imports and registers the vscode-icons collection (idempotent).
 * Safe to call from many components; only the first call does the work.
 */
export function ensureVscodeIcons(): Promise<void> {
  if (registered) return Promise.resolve()
  if (!registerPromise) {
    registerPromise = import('@iconify-json/vscode-icons')
      .then((mod) => {
        addCollection(mod.icons)
        registered = true
        listeners.forEach((listener) => listener())
      })
      .catch((err) => {
        // Allow a later retry if the chunk failed to load.
        registerPromise = null
        throw err
      })
  }
  return registerPromise
}

/** True once the icon collection has been registered. */
export function areVscodeIconsReady(): boolean {
  return registered
}

/**
 * Subscribe (outside React) to the moment the icon collection finishes
 * registering. Fires once per listener when `ensureVscodeIcons()` resolves;
 * returns an unsubscribe. Used by the raw-DOM composer pill to upgrade its
 * neutral fallback glyph to the colored VS Code icon. If already registered the
 * caller should check {@link areVscodeIconsReady} first — this only signals the
 * transition.
 */
export function subscribeVscodeIconsReady(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** Re-renders the caller when the icon collection becomes available. */
export function useIconsReady(): boolean {
  return useSyncExternalStore(
    (onChange) => {
      listeners.add(onChange)
      return () => listeners.delete(onChange)
    },
    () => registered,
    () => registered
  )
}
