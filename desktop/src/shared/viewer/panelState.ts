import type { ViewerTab } from './types'

export interface PanelScope {
  projectKey: string
  threadId: string
}

export type PanelSystemTab = 'changes' | 'plan' | 'subagents' | 'board'
export type PanelActiveTab =
  | { kind: 'system'; id: PanelSystemTab }
  | { kind: 'viewer'; id: string }
  | { kind: 'launcher' }

export interface PanelCheckpoint {
  version: 1
  tabs: ViewerTab[]
  systemTabs: PanelSystemTab[]
  activeTab: PanelActiveTab
  visible: boolean
  fullView: boolean
  width: number
}

export interface PanelStateApi {
  read(scope: PanelScope): Promise<PanelCheckpoint | null>
  update(scope: PanelScope, checkpoint: PanelCheckpoint | null): Promise<void>
}

export function panelScopeKey(scope: PanelScope): string {
  return JSON.stringify([scope.projectKey, scope.threadId])
}
