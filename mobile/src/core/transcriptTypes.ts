import type { ToolGroupLabel } from './toolGroups'
import type { TurnChanges } from './turnChanges'
import type { ImageSource, UserSegment } from './userSegments'

export type ToolVerb =
  | 'ran'
  | 'edited'
  | 'read'
  | 'searched'
  | 'readSkill'
  | 'createdSkill'
  | 'updatedSkill'
  | 'used'
  | 'backgroundStarted'
  | 'backgroundStopped'
  | 'spawned'
  | 'messaged'
  | 'listedAgents'
  | 'stoppedShell'
  | 'stoppedAgent'
  | 'stoppedWorkflow'
  | 'stoppingShell'
  | 'stoppingAgent'
  | 'stoppingWorkflow'
  | 'agentFinished'
  | 'agentInterrupted'

export type ToolIcon = 'terminal' | 'declined' | 'stopped' | 'read' | 'search' | 'folder' | 'edit' | 'web' | 'agent' | 'other'

export type TaskWakeKind = 'terminal' | 'subagent' | 'workflow'

export interface TaskWake {
  task: TaskWakeKind
  label: string
  status: string | null
}

export interface ToolDetail {
  target: string
  output: string | null
}

export type NoticeKind =
  | 'allowedOnce'
  | 'allowedForSession'
  | 'allowedAlways'
  | 'rejected'
  | 'answered'

export type MessageOrigin = 'workflow' | 'automation' | 'app' | 'goal' | 'thread' | 'subagent'

export interface OriginMark {
  kind: MessageOrigin
  label: string
  threadId: string | null
}

export type DividerKind =
  | 'forked'
  | 'remoteConnected'
  | 'remoteLocal'
  | 'remoteDisconnected'
  | 'channelBound'
  | 'channelUnbound'
  | 'compactedAuto'
  | 'compactedManual'
  | 'compactedReactive'
  | 'usageLimit'

export interface ThreadAction {
  kind: 'created' | 'messaged'
  threadId: string
  name: string | null
  queued: boolean
}

export interface TranscriptOptions {
  stopped?: readonly string[]
  spawnedFrom?: string | null
}

export type TranscriptEntry =
  | {
      kind: 'user'
      id: string
      text: string
      segments: UserSegment[]
      images: ImageSource[]
      added: boolean
      origin: OriginMark | null
      goal: boolean
    }
  | {
      kind: 'assistant'
      id: string
      text: string
      streaming: boolean
      phase: 'commentary' | 'final' | null
      at: string
      copy: boolean
      changes: TurnChanges | null
    }
  | {
      kind: 'thinking'
      id: string
      working: boolean
      heading: string | null
    }
  | {
      kind: 'tool'
      id: string
      verb: ToolVerb
      icon: ToolIcon
      subject: string | null
      details: ToolDetail[]
      images: string[]
      startedAt: string
      category: ToolCategory | null
      web: 'search' | 'fetch' | null
      path: string
      created: boolean
      live: boolean
      failed: boolean
      added?: number
      removed?: number
      skill?: { name: string; file: string | null }
    }
  | { kind: 'toolGroup'; id: string; label: ToolGroupLabel; icon: ToolIcon; failed: boolean; active: boolean; children: ToolEntry[] }
  | {
      kind: 'activity'
      id: string
      status: 'working' | 'worked' | 'stopped' | 'youStopped'
      startedAt: string | null
      endedAt: string | null
      origin: TaskWake | null
      children: TranscriptEntry[]
    }
  | { kind: 'notice'; id: string; notice: NoticeKind; detail?: string }
  | { kind: 'failure'; id: string; message: string; overloaded: boolean; retry: { delay: number | null } | null }
  | { kind: 'image'; id: string; status: 'inProgress' | 'completed' | 'failed'; uri: string | null; dropped: boolean; error?: string }
  | { kind: 'plan'; id: string; title: string; overview: string; todos: PlanTodo[]; content: string }
  | { kind: 'changes'; id: string; changes: TurnChanges }
  | { kind: 'divider'; id: string; divider: DividerKind; host?: string; channel?: string; target?: string }
  | { kind: 'threadAction'; id: string; action: ThreadAction }

export type ToolEntry = Extract<TranscriptEntry, { kind: 'tool' }>

export type ToolCategory = 'explore' | 'shell' | 'write' | 'web'

export interface PlanTodo {
  content: string
  status: 'pending' | 'in_progress' | 'completed' | 'cancelled'
}
