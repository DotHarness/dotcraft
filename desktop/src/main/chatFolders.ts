import { randomBytes } from 'crypto'
import { mkdir } from 'fs/promises'
import { isAbsolute, join, normalize, relative, resolve, sep } from 'path'

const MAX_SLUG_WORDS = 6
const MAX_SLUG_LENGTH = 80
const MAX_NUMBERED_SUFFIX = 100
const RANDOM_SUFFIX_ATTEMPTS = 10

export function chatFolderSlug(firstMessage: string): string {
  const words = firstMessage.toLowerCase().match(/[a-z0-9]+/g) ?? []
  const slug = words.slice(0, MAX_SLUG_WORDS).join('-').slice(0, MAX_SLUG_LENGTH).replace(/-+$/, '')
  return slug || 'new-chat'
}

export function normalizeChatsFolderSetting(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  const trimmed = value.trim()
  return trimmed && isAbsolute(trimmed) ? normalize(trimmed) : undefined
}

export function resolveChatsFolderRoot(chatsFolder: string | undefined, documentsPath: string): string {
  return normalizeChatsFolderSetting(chatsFolder) ?? join(documentsPath, 'DotCraft', 'Chats')
}

function uniquePaths(paths: readonly string[]): string[] {
  const seen = new Set<string>()
  return paths.filter((path) => {
    const key = process.platform === 'win32' ? path.toLowerCase() : path
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
}

export function rememberChatsFolder(history: unknown, chatsFolder: string | undefined): string[] {
  const entries = Array.isArray(history) ? history : []
  const paths = [...entries, chatsFolder].map(normalizeChatsFolderSetting).filter((path): path is string => path !== undefined)
  return uniquePaths(paths)
}

export function knownChatsFolderRoots(
  settings: { chatsFolder?: string; chatsFolderHistory?: string[] },
  documentsPath: string
): string[] {
  return uniquePaths([
    resolveChatsFolderRoot(settings.chatsFolder, documentsPath),
    resolveChatsFolderRoot(undefined, documentsPath),
    ...rememberChatsFolder(settings.chatsFolderHistory, undefined)
  ])
}

export function chatFolderContaining(targetPath: string, root: string): string | null {
  const base = resolve(root)
  const rel = relative(base, resolve(targetPath))
  if (!rel || isAbsolute(rel)) return null
  const [date, slug] = rel.split(sep)
  if (!slug || date === '..') return null
  return join(base, date, slug)
}

function localDateFolderName(now: Date): string {
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

function* candidateNames(slug: string): Generator<string> {
  yield slug
  for (let suffix = 2; suffix <= MAX_NUMBERED_SUFFIX; suffix++) yield `${slug}-${suffix}`
  for (let attempt = 0; attempt < RANDOM_SUFFIX_ATTEMPTS; attempt++) {
    yield `${slug}-${randomBytes(3).toString('hex')}`
  }
}

export async function createChatFolder(
  root: string,
  firstMessage: string,
  now: Date = new Date()
): Promise<string> {
  const dateFolder = join(root, localDateFolderName(now))
  await mkdir(dateFolder, { recursive: true })
  for (const name of candidateNames(chatFolderSlug(firstMessage))) {
    const folder = join(dateFolder, name)
    try {
      await mkdir(folder)
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'EEXIST') continue
      throw error
    }
    await mkdir(join(folder, 'work'))
    await mkdir(join(folder, 'outputs'))
    return folder
  }
  throw new Error(`Could not create a chat folder in ${dateFolder}.`)
}
