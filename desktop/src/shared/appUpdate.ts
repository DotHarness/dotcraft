export type AppUpdateStatus =
  | 'unsupported'
  | 'idle'
  | 'checking'
  | 'not-available'
  | 'available'
  | 'downloading'
  | 'downloaded'
  | 'preparing'
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
  bytesPerSecond: number
}

export interface AppUpdateState {
  status: AppUpdateStatus
  currentVersion: string
  update?: AppUpdateInfo
  progress?: AppUpdateProgress
  error?: string
  retryAction?: 'download' | 'install'
}
