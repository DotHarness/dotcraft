import { useEffect, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useFeatureGuideStore } from '../../stores/featureGuideStore'
import type { QueuedTurnInput } from '../../types/conversation'
import { FeatureGuideCapture, FeatureGuideCoachmark, FeatureGuideMedia } from './FeatureGuide'
import sideChatDark from '../../assets/featureGuides/side-chat-dark.webp'
import sideChatLight from '../../assets/featureGuides/side-chat-light.webp'

const MEDIA = { light: sideChatLight, dark: sideChatDark }
const GUIDE_STYLE: CSSProperties = { right: 0, bottom: 'calc(100% + 10px)', zIndex: 20, pointerEvents: 'auto' }
const SIDE_CHAT_MIN_QUEUED = 2
// Counted per app session: the guide answers waiting, so it needs no history.
const queuedThisSession = new Set<string>()
let eligibleMounts = 0

export function resetSideChatGuideSignal(): void {
  queuedThisSession.clear()
}

export function SideChatGuideCard({ onAsk, onClose, style }: { onAsk: () => void; onClose: () => void; style?: CSSProperties }): JSX.Element {
  const t = useT()
  return (
    <FeatureGuideCoachmark
      style={style}
      media={(
        <FeatureGuideMedia focus="70% 40%" height={168}>
          <FeatureGuideCapture source={MEDIA} style={{ left: 24, top: 18, width: 270 }} />
        </FeatureGuideMedia>
      )}
      title={t('featureGuides.sideChat.title')}
      body={t('featureGuides.sideChat.body')}
      primaryLabel={t('featureGuides.sideChat.action')}
      onPrimary={onAsk}
      onClose={onClose}
    />
  )
}

export function SideChatFeatureGuide({ queuedInputs, onSendInSideChat }: {
  queuedInputs: readonly QueuedTurnInput[]
  onSendInSideChat?: (queuedInputId: string) => void
}): JSX.Element | null {
  const waiting = queuedInputs.filter((item) => item.status === 'queued')
  const latest = waiting.at(-1)?.id ?? null
  const waitingKey = waiting.map((item) => item.id).join('|')
  const [queuedCount, setQueuedCount] = useState(queuedThisSession.size)
  const active = useFeatureGuideStore((state) => state.active === 'sideChat')
  const eligible = onSendInSideChat != null && latest != null && queuedCount >= SIDE_CHAT_MIN_QUEUED

  useEffect(() => {
    for (const id of waitingKey.split('|')) if (id) queuedThisSession.add(id)
    setQueuedCount(queuedThisSession.size)
  }, [waitingKey])

  useEffect(() => {
    if (!eligible) return
    eligibleMounts += 1
    useFeatureGuideStore.getState().request('sideChat')
    return () => {
      eligibleMounts -= 1
      queueMicrotask(() => {
        const state = useFeatureGuideStore.getState()
        if (eligibleMounts === 0 && state.active === 'sideChat') state.close('sideChat', false)
      })
    }
  }, [eligible])

  if (!active || !eligible || latest == null) return null
  const close = (acted: boolean): void => useFeatureGuideStore.getState().close('sideChat', acted)
  return (
    <SideChatGuideCard
      style={GUIDE_STYLE}
      onAsk={() => {
        close(true)
        onSendInSideChat?.(latest)
      }}
      onClose={() => close(false)}
    />
  )
}
