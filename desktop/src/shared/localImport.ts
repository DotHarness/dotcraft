import type {
  ImportCandidate,
  ImportCompletedNotification,
  ImportOutcome,
  ImportProgressNotification,
  ImportSelection,
  ImportSettings,
  ImportSettingsSetParams
} from '@dotcraft/sdk/contracts'

export interface LocalImportItem {
  key: string
  candidate: ImportCandidate
  projectPath: string | null
  projectName: string | null
  destination: 'personal' | 'project' | 'newProject' | 'chats' | 'unavailable'
}

export interface LocalImportSource {
  source: string
  available: boolean
  items: LocalImportItem[]
  importableCount: number
}

export interface LocalImportRunParams {
  source: string
  items: Array<{ key: string; fingerprint: string }>
  selection: ImportSelection
  offered: ImportSelection
}

export type LocalImportOutcome = ImportOutcome & { projectPath?: string | null; projectName?: string | null }
export interface LocalImportBatch extends ImportCompletedNotification {
  outcomes: LocalImportOutcome[]
}

export interface LocalImportState {
  enabled: boolean
  settings: ImportSettings
  running: ImportProgressNotification | null
  imports: LocalImportBatch[]
  attention: LocalImportOutcome[]
}

export interface LocalImportApi {
  detect(): Promise<{ sources: LocalImportSource[] }>
  getState(): Promise<LocalImportState>
  run(params: LocalImportRunParams): Promise<{ importId: string }>
  setSettings(params: ImportSettingsSetParams): Promise<{ settings: ImportSettings }>
}
