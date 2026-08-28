/**
 * Shared types for the Desktop Viewer Panel.
 * Used by main process (IPC handlers) and renderer (store, components).
 */

export type ViewerKind = 'files' | 'file' | 'browser' | 'terminal' | 'workflow'

export type ViewerContentClass = 'text' | 'image' | 'pdf' | 'unsupported'

export interface FileNavigationHint {
  line?: number
  column?: number
  fragment?: string
  query?: string
}

interface ViewerTabBase {
  /** Stable id created at tab-open time. */
  id: string
  kind: ViewerKind
  label: string
  /** If set, the tab renders an in-tab error state instead of the viewer body. */
  errorMessage?: string
}

/** Empty workspace file-viewer tab descriptor. */
export interface FilesViewerTab extends ViewerTabBase {
  kind: 'files'
}

export interface FileViewerTab extends ViewerTabBase {
  kind: 'file'
  /** Normalized absolute path (realpath-resolved by main). */
  absolutePath: string
  /** Workspace-relative path used for label derivation. */
  relativePath: string
  contentClass: ViewerContentClass
  /** File size in bytes at classification time; used by image viewer for info display. */
  sizeBytes?: number
  /** Optional deep-link navigation hint (line/column/query/fragment). */
  navigationHint?: FileNavigationHint
  /**
   * Per-tab word-wrap preference for the text viewer. Undefined is treated as
   * enabled (the historical default); toggled via the viewer `…` menu.
   */
  wordWrap?: boolean
}

export interface BrowserViewerTab extends ViewerTabBase {
  kind: 'browser'
  /**
   * Stable browser target id. Kept separate from currentUrl so deep-linking
   * can reference this tab regardless of navigation changes.
   */
  target: string
  currentUrl: string
  title?: string
  /** Last-known favicon (data URL). */
  faviconDataUrl?: string
  loading: boolean
  canGoBack: boolean
  canGoForward: boolean
  /** True when the webContents renderer crashed and requires reload. */
  crashed?: boolean
  /** User-facing notice for blocked navigation attempts. */
  blockedMessage?: string
  /** User-facing notice when a download is blocked/cancelled. */
  downloadMessage?: string
  /** True while an agent is actively operating this embedded browser tab. */
  automationActive?: boolean
  /** Last browser session name supplied by the agent. */
  automationSessionName?: string
  /** Concise description of the latest automation action. */
  lastAutomationAction?: string
  /** Last known virtual cursor location in viewport coordinates. */
  virtualCursor?: BrowserVirtualCursor
}

export interface BrowserVirtualCursor {
  x: number
  y: number
}

export interface TerminalExitState {
  code: number | null
  signal: number | null
}

export interface TerminalViewerTab extends ViewerTabBase {
  kind: 'terminal'
  cwd: string
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

export type ViewerTab = FilesViewerTab | FileViewerTab | BrowserViewerTab | TerminalViewerTab | WorkflowViewerTab

export interface PerThreadViewerState {
  /** Ordered list of open viewer tabs (insertion order). */
  tabs: ViewerTab[]
  activeTabId: string | null
}

/** Result returned by `workspace:viewer:classify`. */
export interface ClassifyResult {
  contentClass: ViewerContentClass
  /** MIME type hint derived from extension / magic bytes. */
  mime: string
  /** File size in bytes at classification time. */
  sizeBytes: number
}

/** Result returned by `workspace:viewer:read-text`. */
export interface ReadTextResult {
  text: string
  /** True if the file was truncated to stay within `limitBytes`. */
  truncated: boolean
  /** Encoding that was used (currently always 'utf-8'). */
  encoding: string
}

/** Parameters for `workspace:viewer:list-files`. */
export interface ListFilesParams {
  workspacePath: string
  query: string
  limit: number
}

/** Parameters for `workspace:viewer:classify`. */
export interface ClassifyParams {
  absolutePath: string
}

/** Parameters for `workspace:viewer:read-text`. */
export interface ReadTextParams {
  absolutePath: string
  limitBytes?: number
}

/** A single directory entry returned by `workspace:viewer:list-dir`. */
export interface DirEntryWire {
  name: string
  /** Workspace-relative POSIX path (forward slashes). */
  relativePath: string
  absolutePath: string
  isDir: boolean
}

/** Parameters for `workspace:viewer:list-dir`. */
export interface ListDirParams {
  /** Absolute directory to list. Defaults to the workspace root when omitted. */
  dirPath?: string
}

/** Result returned by `workspace:viewer:list-dir`. */
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
  | 'virtual-cursor'

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
  x?: number
  y?: number
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
