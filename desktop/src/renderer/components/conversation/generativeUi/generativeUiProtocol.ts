export const GENERATIVE_UI_PROTOCOL_VERSION = 1

export const GENERATIVE_UI_RUNNER_READY_METHOD = 'generativeUi/runnerReady'
export const GENERATIVE_UI_CONNECT_METHOD = 'generativeUi/connect'
export const GENERATIVE_UI_WORKER_SOURCE_ELEMENT_ID = 'generative-ui-worker'

export const GENERATIVE_UI_COMPONENT_NAMES = [
  'Card', 'Row', 'Col', 'Box', 'Spacer', 'Divider',
  'Title', 'Text', 'Caption', 'Markdown', 'Badge', 'Icon', 'Image',
  'Button', 'Input', 'Textarea', 'Select', 'Checkbox', 'RadioGroup', 'SegmentedControl', 'Slider', 'Form',
  'Table', 'List', 'Chart', 'PieChart'
] as const

export type GenerativeUiComponentName = (typeof GENERATIVE_UI_COMPONENT_NAMES)[number]

export const GENERATIVE_UI_LIMITS = {
  maxSourceBytes: 64 * 1024,
  maxSavedStateBytes: 16 * 1024,
  maxSnapshotBytes: 256 * 1024,
  maxNodes: 1500,
  maxDepth: 32,
  maxPromptBytes: 16 * 1024,
  maxActionTextBytes: 64 * 1024,
  maxUrlLength: 2048,
  maxErrorMessageLength: 1000,
  frameTimeoutMs: 10_000,
  firstSnapshotTimeoutMs: 5_000,
  workerUnresponsiveMs: 3_000,
  workerPingIntervalMs: 500,
  maxCallbacksPerSecond: 30,
  actionWindowMs: 1_000,
  maxSynchronousRenders: 50
} as const

export type GenerativeUiTheme = 'light' | 'dark'
export type GenerativeUiBreakpoint = 'compact' | 'regular' | 'wide'

export function generativeUiBreakpointForWidth(width: number): GenerativeUiBreakpoint {
  if (width < 480) return 'compact'
  return width < 900 ? 'regular' : 'wide'
}

export type GenerativeUiFailureStage =
  | 'frame_timeout'
  | 'worker_timeout'
  | 'worker_start_error'
  | 'protocol_mismatch'
  | 'compile_error'
  | 'runtime_error'
  | 'callback_error'
  | 'host_error'
  | 'invalid_payload'
  | 'payload_too_large'

export const GENERATIVE_UI_RUNNER_FAILURE_STAGES: ReadonlySet<GenerativeUiFailureStage> = new Set([
  'worker_timeout',
  'worker_start_error',
  'protocol_mismatch',
  'compile_error',
  'runtime_error',
  'callback_error',
  'invalid_payload',
  'payload_too_large'
])

export interface GenerativeUiFailure {
  stage: GenerativeUiFailureStage
  message: string
  line?: number
  column?: number
}

export type GenerativeUiJson = string | number | boolean | null | GenerativeUiJson[] | { [key: string]: GenerativeUiJson }

export interface GenerativeUiElementNode {
  type: string
  key?: string
  props: Record<string, GenerativeUiJson>
  children: GenerativeUiNode[]
}

export type GenerativeUiNode = string | GenerativeUiElementNode

export type GenerativeUiSavedState = Record<string, Record<string, GenerativeUiJson>>

export type GenerativeUiActionKind = 'sendMessage' | 'prefillComposer' | 'copy' | 'openUrl'

export type GenerativeUiAction =
  | { kind: 'sendMessage' | 'prefillComposer' | 'copy'; text: string }
  | { kind: 'openUrl'; url: string }

export type GenerativeUiHostMessage =
  | { protocolVersion: number; type: 'start'; code: string; theme: GenerativeUiTheme; breakpoint: GenerativeUiBreakpoint; initialState?: GenerativeUiSavedState }
  | { protocolVersion: number; type: 'callback'; id: string; args: GenerativeUiJson[] }
  | { protocolVersion: number; type: 'globals'; theme: GenerativeUiTheme; breakpoint: GenerativeUiBreakpoint }
  | { protocolVersion: number; type: 'restore'; state: GenerativeUiSavedState }
  | { protocolVersion: number; type: 'dispose' }

export type GenerativeUiRunnerMessage =
  | { protocolVersion: number; type: 'snapshot'; version: number; tree: GenerativeUiNode[] }
  | { protocolVersion: number; type: 'action'; kind: GenerativeUiActionKind; payload: { text?: string; url?: string } }
  | { protocolVersion: number; type: 'state'; state: GenerativeUiSavedState }
  | { protocolVersion: number; type: 'error'; stage: GenerativeUiFailureStage; message: string; line?: number; column?: number }

export function generativeUiActionLimit(kind: GenerativeUiActionKind): number {
  return kind === 'sendMessage' ? GENERATIVE_UI_LIMITS.maxPromptBytes : GENERATIVE_UI_LIMITS.maxActionTextBytes
}

export function isGenerativeUiJson(value: unknown, depth = 0): value is GenerativeUiJson {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return true
  if (typeof value === 'number') return Number.isFinite(value)
  if (depth > 32 || typeof value !== 'object') return false
  if (Array.isArray(value)) return value.every(item => isGenerativeUiJson(item, depth + 1))
  if (Object.getPrototypeOf(value) !== Object.prototype) return false
  return Object.values(value).every(item => isGenerativeUiJson(item, depth + 1))
}

export function readGenerativeUiSavedState(value: unknown): GenerativeUiSavedState | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  for (const slots of Object.values(value)) {
    if (!slots || typeof slots !== 'object' || Array.isArray(slots) || !isGenerativeUiJson(slots)) return null
  }
  return value as GenerativeUiSavedState
}

export function generativeUiJsonBytes(value: unknown): number {
  try {
    const json = JSON.stringify(value)
    return json === undefined ? Number.POSITIVE_INFINITY : new TextEncoder().encode(json).byteLength
  } catch {
    return Number.POSITIVE_INFINITY
  }
}
