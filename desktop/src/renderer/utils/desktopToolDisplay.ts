import {
  Archive,
  ArchiveRestore,
  CircleStop,
  FolderOpen,
  FolderPlus,
  MessageSquarePlus,
  MessageSquareText,
  Pencil,
  Pin,
  PinOff,
  Search,
  X,
  type LucideIcon
} from 'lucide-react'
import { TerminalGlyph } from '../components/ui/TerminalGlyph'
import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { isDesktopToolNamespace } from './threadToolDisplay'

export const DESKTOP_TOOL_OPERATIONS = [
  'readChat',
  'listChats',
  'listProjects',
  'createProject',
  'searchChats',
  'sendMessage',
  'stopChat',
  'createChat',
  'renameChat',
  'archiveChat',
  'restoreChat',
  'pinChat',
  'unpinChat',
  'readTerminal',
  'listTerminals',
  'dismissSuggestion'
] as const

export type DesktopToolOperation = (typeof DESKTOP_TOOL_OPERATIONS)[number]

type DesktopToolState = 'active' | 'completed' | 'failed'

type DesktopToolSource = Pick<ConversationItem, 'toolName'> & Partial<Pick<ConversationItem, 'type' | 'pluginNamespace'>>

const OPERATIONS_BY_TOOL: Readonly<Record<string, DesktopToolOperation>> = {
  ReadThread: 'readChat',
  ListThreadItems: 'readChat',
  ReadThreadItem: 'readChat',
  ListThreads: 'listChats',
  ListProjects: 'listProjects',
  CreateProject: 'createProject',
  SearchThreadMessages: 'searchChats',
  SendMessageToThread: 'sendMessage',
  StopThread: 'stopChat',
  CreateThread: 'createChat',
  SetThreadTitle: 'renameChat',
  SetThreadArchived: 'archiveChat',
  SetThreadPinned: 'pinChat',
  ReadTerminal: 'readTerminal',
  ListTerminalTabs: 'listTerminals',
  DismissProposal: 'dismissSuggestion'
}

const OPERATIONS_OPENING_CHAT: ReadonlySet<DesktopToolOperation> = new Set(['readChat', 'sendMessage', 'stopChat'])

const ICONS: Readonly<Record<DesktopToolOperation, LucideIcon>> = {
  readChat: MessageSquareText,
  listChats: MessageSquareText,
  listProjects: FolderOpen,
  createProject: FolderPlus,
  searchChats: Search,
  sendMessage: MessageSquareText,
  stopChat: CircleStop,
  createChat: MessageSquarePlus,
  renameChat: Pencil,
  archiveChat: Archive,
  restoreChat: ArchiveRestore,
  pinChat: Pin,
  unpinChat: PinOff,
  readTerminal: TerminalGlyph,
  listTerminals: TerminalGlyph,
  dismissSuggestion: X
}

/** A call still streaming its arguments has no namespace yet, so an absent one counts as Desktop's. */
export function resolveDesktopToolOperation(
  item: DesktopToolSource,
  args: Record<string, unknown> | undefined
): DesktopToolOperation | null {
  if (item.type === 'mcpToolCall') return null
  if (!isDesktopToolNamespace(item.pluginNamespace)) return null
  const operation = item.toolName ? OPERATIONS_BY_TOOL[item.toolName] : undefined
  if (!operation) return null
  if (operation === 'archiveChat' && args?.archived === false) return 'restoreChat'
  if (operation === 'pinChat' && args?.pinned === false) return 'unpinChat'
  return operation
}

export function formatDesktopToolLabel(
  operation: DesktopToolOperation,
  state: DesktopToolState,
  locale: AppLocale
): string {
  return translate(locale, `toolCall.desktop.${operation}.${state}`)
}

export function getDesktopToolIcon(operation: DesktopToolOperation): LucideIcon {
  return ICONS[operation]
}

export function getDesktopToolTargetThreadId(
  operation: DesktopToolOperation,
  args: Record<string, unknown> | undefined
): string | null {
  if (!OPERATIONS_OPENING_CHAT.has(operation)) return null
  const threadId = args?.threadId
  return typeof threadId === 'string' && threadId.trim() ? threadId : null
}

export function getDesktopToolTargetProjectPath(
  operation: DesktopToolOperation,
  structuredResult: unknown
): string | null {
  if (operation !== 'createProject' || !structuredResult || typeof structuredResult !== 'object') return null
  const project = (structuredResult as { project?: unknown }).project
  if (!project || typeof project !== 'object') return null
  const projectPath = (project as { path?: unknown }).path
  return typeof projectPath === 'string' && projectPath.trim() ? projectPath.trim() : null
}
