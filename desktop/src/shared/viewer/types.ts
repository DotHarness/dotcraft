export type ViewerKind = 'files' | 'file' | 'browser' | 'terminal' | 'workflow' | 'automation' | 'sideChat'
export type * from './browserFeedback'

export type ViewerContentClass = 'text' | 'image' | 'pdf' | 'unsupported'

export interface FileNavigationHint {
  line?: number
  endLine?: number
  column?: number
  fragment?: string
  query?: string
}

interface ViewerTabBase {
  id: string
  kind: ViewerKind
  label: string
  errorMessage?: string
}

export interface FilesViewerTab extends ViewerTabBase {
  kind: 'files'
}

export interface FileViewerTab extends ViewerTabBase {
  kind: 'file'
  /** Normalized absolute path (realpath-resolved by main). */
  absolutePath: string
  relativePath: string
  contentClass: ViewerContentClass
  sizeBytes?: number
  navigationHint?: FileNavigationHint
  /** Increments for explicit opens, even when the target position is unchanged. */
  navigationRevision?: number
  wordWrap?: boolean
}

export interface BrowserViewerTab extends ViewerTabBase {
  kind: 'browser'
  target: string
  currentUrl: string
  title?: string
  faviconDataUrl?: string
  loading: boolean
  canGoBack: boolean
  canGoForward: boolean
  crashed?: boolean
  blockedMessage?: string
  downloadMessage?: string
  automationActive?: boolean
  automationSessionName?: string
  lastAutomationAction?: string
}

export interface TerminalExitState {
  code: number | null
  signal: number | null
}

export interface TerminalViewerTab extends ViewerTabBase {
  kind: 'terminal'
  cwd: string
  title?: string
  /** A command waiting to restart this tab's shell; `nonce` distinguishes repeated runs. */
  pendingRun?: { command: string; env?: Record<string, string>; nonce: number }
  shell?: string
  pid?: number
  exited?: TerminalExitState
  hasStarted: boolean
}

export interface WorkflowViewerTab extends ViewerTabBase {
  kind: 'workflow'
  threadId: string
  runId: string
}

export interface AutomationViewerTab extends ViewerTabBase {
  kind: 'automation'
  threadId: string
  automationId: string
}

export interface SideChatViewerTab extends ViewerTabBase {
  kind: 'sideChat'
  threadId: string
  sideChatKey: string
}

export type ViewerTab =
  | FilesViewerTab
  | FileViewerTab
  | BrowserViewerTab
  | TerminalViewerTab
  | WorkflowViewerTab
  | AutomationViewerTab
  | SideChatViewerTab

export interface PerThreadViewerState {
  tabs: ViewerTab[]
  activeTabId: string | null
}

export interface ClassifyResult {
  contentClass: ViewerContentClass
  mime: string
  sizeBytes: number
}

export interface ReadTextResult {
  text: string
  truncated: boolean
  encoding: string
  sizeBytes: number
  mtimeMs: number
  hasUtf8Bom: boolean
  lineEnding: 'lf' | 'crlf' | 'cr'
  readOnlyReason?: 'large-file'
}

export interface ListFilesParams {
  workspacePath: string
  query: string
  limit: number
}

export interface ClassifyParams {
  absolutePath: string
}

export interface ReadTextParams {
  absolutePath: string
  limitBytes?: number
}

export interface WriteTextParams {
  absolutePath: string
  text: string
  expectedMtimeMs: number
  hasUtf8Bom: boolean
  lineEnding: 'lf' | 'crlf' | 'cr'
}

export type WriteTextResult =
  | { outcome: 'saved'; mtimeMs: number; sizeBytes: number }
  | { outcome: 'conflict'; current: ReadTextResult }

export interface TextFileChangedPayload {
  absolutePath: string
  mtimeMs: number
  sizeBytes: number
}

export interface DirEntryWire {
  name: string
  /** Workspace-relative POSIX path (forward slashes). */
  relativePath: string
  absolutePath: string
  isDir: boolean
}

export interface ListDirParams {
  /** Absolute directory to list. Defaults to the workspace root when omitted. */
  dirPath?: string
}

export interface ListDirResult {
  dirPath: string
  /** Immediate children, directories first then files, each alpha-sorted. */
  entries: DirEntryWire[]
}

export interface BrowserCreateParams {
  tabId: string
  threadId?: string
  workspacePath: string
  initialUrl?: string
}

export interface BrowserNavigateParams {
  tabId: string
  url: string
}

export interface BrowserBoundsParams {
  tabId: string
  x: number
  y: number
  width: number
  height: number
}

export interface TerminalCreateParams {
  tabId: string
  threadId: string
  workspacePath: string
  cols: number
  rows: number
}

export interface TerminalWriteParams {
  tabId: string
  data: string
}

export interface TerminalResizeParams {
  tabId: string
  cols: number
  rows: number
}

export interface TerminalAttachParams {
  tabId: string
}

export interface TerminalDisposeParams {
  tabId: string
}

export interface TerminalAttachResult {
  tabId: string
  pid: number
  shell: string
  cwd: string
  buffer: string
  exited?: TerminalExitState
}

export interface TerminalCreateResult {
  tabId: string
  pid: number
  shell: string
  cwd: string
}

export type BrowserEventType =
  | 'did-start-loading'
  | 'did-stop-loading'
  | 'did-navigate'
  | 'did-fail-load'
  | 'page-title-updated'
  | 'page-favicon-updated'
  | 'blocked-navigation'
  | 'download-blocked'
  | 'request-new-tab'
  | 'crashed'
  | 'update-history-flags'
  | 'external-handoff'
  | 'automation-started'
  | 'automation-updated'
  | 'automation-stopped'

export interface BrowserEventPayload {
  tabId: string
  threadId?: string
  type: BrowserEventType
  url?: string
  title?: string
  faviconDataUrl?: string
  canGoBack?: boolean
  canGoForward?: boolean
  message?: string
  errorCode?: number
  errorDescription?: string
  validatedURL?: string
  finalURL?: string
  isMainFrame?: boolean
  automationActive?: boolean
  sessionName?: string
  action?: string
}

export interface BrowserUseOpenPayload {
  threadId: string
  tabId: string
  initialUrl: string
  title?: string
  focusMode: 'first-open' | 'none'
}

export interface BrowserUseClosePayload {
  threadId: string
  tabId: string
}

export type BrowserUseApprovalResponseAction = 'allowOnce' | 'allowDomain' | 'blockDomain' | 'deny'

export interface BrowserUseApprovalRequestPayload {
  requestId: string
  threadId: string
  tabId: string
  url: string
  domain: string
  sessionName?: string
}

export interface BrowserUseApprovalResponsePayload {
  requestId: string
  action: BrowserUseApprovalResponseAction
}

export type TerminalEventType = 'data' | 'exit'

export interface TerminalDataEventPayload {
  tabId: string
  type: 'data'
  data: string
}

export interface TerminalExitEventPayload {
  tabId: string
  type: 'exit'
  code: number | null
  signal: number | null
}

export type TerminalEventPayload = TerminalDataEventPayload | TerminalExitEventPayload
