export type AppUpdateStatus =
  | 'unsupported'
  | 'idle'
  | 'checking'
  | 'not-available'
  | 'available'
  | 'downloading'
  | 'downloaded'
  | 'error'

export interface AppUpdateInfo {
  latestVersion: string
  releaseNotes?: string
  htmlUrl: string
}

export interface AppUpdateProgress {
  transferredBytes: number
  totalBytes: number
  percent: number
}

export interface AppUpdateState {
  status: AppUpdateStatus
  currentVersion: string
  update?: AppUpdateInfo
  progress?: AppUpdateProgress
  error?: string
}
