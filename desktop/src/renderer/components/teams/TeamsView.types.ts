export type TeamMember = {
  memberId: string
  role: string
  displayName: string
  description: string
  threadId?: string | null
  bindingId: string
  avatarAccent: string
  status: string
  currentTaskId?: string | null
  deskX: number
  deskY: number
  queuedInputCount?: number
  running?: boolean
  waitingOnApproval?: boolean
  waitingOnInput?: boolean
}

export type Mission = {
  missionId: string
  title: string
  prompt: string
  plan?: string
  status: string
  leaderThreadId?: string | null
  createdAt: string
  updatedAt: string
  completedAt?: string | null
  completionSummary?: string | null
  finalResponse?: string | null
  scratchpadPath?: string | null
  archivedAt?: string | null
}

export type TeamTask = {
  taskId: string
  missionId: string
  assigneeMemberId: string
  title: string
  prompt: string
  status: string
  kind?: string
  requiredForMission?: boolean
  requiresLeaderSynthesis?: boolean
  dependsOnTaskIds?: string[]
  blockedOnTaskIds?: string[]
  blockedReason?: string | null
  queuedInputId?: string | null
  synthesisMessageId?: string | null
  completionRecoveryPending?: boolean
  completionRecoveryQueuedInputId?: string | null
  completionRecoveryAttempts?: number
  digest?: string
  createdAt: string
  updatedAt?: string
}

export type TeamMessage = {
  messageId: string
  missionId: string
  fromMemberId: string
  toMemberId: string
  taskId?: string | null
  content: string
  kind?: string
  requiresAction?: boolean
  status?: string
  artifactIds?: string[]
  deliveredQueuedInputId?: string | null
  deliveredAt?: string | null
  createdAt: string
}

export type ArtifactRef = {
  artifactId: string
  taskId: string
  memberId: string
  title: string
  uri: string
  description?: string
}

export type MailboxDigest = {
  digestId: string
  memberId: string
  content: string
  updatedAt: string
}

export type MissionThread = {
  missionId: string
  memberId: string
  threadId: string
  bindingId: string
  grantId: string
  status: string
  currentTaskId?: string | null
  queuedInputId?: string | null
  createdAt: string
  updatedAt: string
  archivedAt?: string | null
  queuedInputCount?: number
  running?: boolean
  waitingOnApproval?: boolean
  waitingOnInput?: boolean
}

export type TeamView = {
  team: {
    enabled: boolean
    updatedAt?: string
  }
  stats: {
    runningMembers: number
    queuedInputs: number
    totalTasks: number
    completedTasks: number
    inputTokens: number
    outputTokens: number
    cachedInputTokens: number
    totalTokens: number
  }
  members: TeamMember[]
  missions: Mission[]
  archivedMissions: Mission[]
  missionThreads: MissionThread[]
  tasks: TeamTask[]
  messages: TeamMessage[]
  artifacts: ArtifactRef[]
  mailboxDigests: MailboxDigest[]
}

export type CardKind = 'draft' | 'mission' | 'task' | 'member'
export type CardStatusChipTone = 'live' | 'queued' | 'done' | 'cancelled'
export type DiscardAction = 'cancel' | 'archive'
export type ActorPhase = 'idle' | 'traveling' | 'meeting' | 'settling' | 'working'
export type ActorTargetKind = 'mission' | 'task'
export type IntentKind = 'plan' | 'talk' | 'task' | 'work' | 'rest'

export type SpawnFlipParams = {
  fromX: number
  fromY: number
  arcX?: number
  spinFrom?: number
  spinMid?: number
}

export type SelectedCard =
  | { kind: 'draft' }
  | { kind: 'mission'; id: string }
  | { kind: 'task'; id: string }
  | { kind: 'member'; id: string }
  | { kind: 'archivePile' }
  | { kind: 'historyMission'; id: string }

export type OpenThreadParams = {
  taskId?: string
  missionId?: string
  memberId?: string
}

export type BoardCard = {
  key: string
  kind: CardKind
  id?: string
  title: string
  status: string
  body: string
  x: number
  y: number
  rotation: number
  z: number
  stripLabel: string
  stripMeta: string
  note?: string
  progress?: number
  statusChip?: {
    label: string
    tone: CardStatusChipTone
  }
  completed?: boolean
  spawned?: boolean
  settling?: boolean
  working?: boolean
  discardAction?: DiscardAction
  actorPhase?: ActorPhase
  actorTargetKey?: string
  memberId?: string
  missionId?: string
  taskId?: string
  openThreadParams?: OpenThreadParams
  roleKey?: string
  avatarSrc?: string
  accent?: string
  intent?: IntentKind
  intentLabel?: string
  dialog?: string
  spawnFlip?: SpawnFlipParams
}

export type CardOverride = {
  x: number
  y: number
  rotation: number
  z: number
}

export type ActorTarget = {
  key: string
  kind: ActorTargetKind
  id: string
  x: number
  y: number
  rotation: number
  missionId?: string
  taskId?: string
  missionThread?: MissionThread
  status: string
  stripMeta: string
  working: boolean
  openThreadParams?: OpenThreadParams
}

export type ActorState = {
  memberId: string
  x: number
  y: number
  rotation: number
  homeX: number
  homeY: number
  homeRotation: number
  phase: ActorPhase
  targetKey?: string
  targetKind?: ActorTargetKind
  targetId?: string
  travelId: number
  travelFromX?: number
  travelFromY?: number
  travelFromRotation?: number
  /** Active front-end-only exchange animation id. */
  exchangeId?: string
  /** When the actor is heading to meet another member for an exchange. */
  meetingWith?: string
  meetingDialog?: string
}

export type BoardModel = {
  cards: BoardCard[]
  actorTargets: Record<string, ActorTarget>
}

export type DragState = {
  key: string
  grabX: number
  grabY: number
  moved: boolean
  startOverride: CardOverride
  dragZ?: number
  stackTopKey?: string
  stackTopStartOverride?: CardOverride
  stackTopDragZ?: number
  lastClientX: number
  lastClientY: number
}
