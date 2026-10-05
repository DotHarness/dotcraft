import { NativeModule, requireNativeModule } from 'expo'

interface LiveOngoing {
  title: string
  text: string
  subText: string
  chip: string
  url: string
  end: string
}

interface LiveChannels {
  session: string
  requests: string
  results: string
}

interface LivePost {
  id: string
  channel: 'requests' | 'results'
  title: string
  text: string
  url: string
  alert: boolean
  key?: string
  requestId?: string
  allow?: string
  reject?: string
}

type LiveSessionEvents = {
  action(event: { action: 'allow' | 'reject' | 'end'; key: string | null; requestId: string | null }): void
}

declare class LiveSessionModule extends NativeModule<LiveSessionEvents> {
  notificationsEnabled(): boolean
  start(ongoing: LiveOngoing, channels: LiveChannels): boolean
  update(ongoing: LiveOngoing): void
  stop(): void
  post(notice: LivePost): void
  cancel(id: string): void
}

export default requireNativeModule<LiveSessionModule>('LiveSession')
