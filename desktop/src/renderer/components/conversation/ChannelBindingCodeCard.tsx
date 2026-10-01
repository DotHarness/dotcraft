import { useEffect, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useAppBindingStore } from '../../stores/appBindingStore'
import { useChannelBindingStore, type PendingChannelBinding } from '../../stores/channelBindingStore'
import { addToast } from '../../stores/toastStore'
import { Button } from '../ui/Button'
import { ChannelIconBadge, getChannelVisualMeta } from '../ui/channelMeta'
import styles from './ChannelBindingCodeCard.module.css'

const COPIED_RESET_MS = 1500
const SETTLED_STATES = new Set(['active', 'revoked', 'failed', 'cancelled'])

export function ChannelBindingCodeCard({ threadId }: { threadId: string }): JSX.Element | null {
  const pending = useChannelBindingStore((s) => s.pendingByThread[threadId])
  if (!pending) return null
  return <PendingCodeCard key={pending.bindingId} pending={pending} />
}

function PendingCodeCard({ pending }: { pending: PendingChannelBinding }): JSX.Element {
  const t = useT()
  const [now, setNow] = useState(() => Date.now())
  const [copied, setCopied] = useState(false)
  const settled = useAppBindingStore((s) => (s.bindingsByThread[pending.threadId] ?? [])
    .some((binding) => binding.bindingId === pending.bindingId && SETTLED_STATES.has(binding.state)))
  const channel = getChannelVisualMeta(pending.channelName).label
  const command = `/bind ${pending.code}`
  const remainingMs = Date.parse(pending.expiresAt) - now

  useEffect(() => {
    if (settled) useChannelBindingStore.getState().clearPending(pending.threadId, pending.bindingId)
  }, [pending.bindingId, pending.threadId, settled])

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  useEffect(() => {
    if (Number.isNaN(remainingMs) || remainingMs > 0) return
    void useChannelBindingStore.getState().cancelPending(pending.threadId)
    addToast(t('channelBinding.pending.expired', { channel }), 'info')
  }, [channel, pending.threadId, remainingMs, t])

  useEffect(() => {
    if (!copied) return
    const timer = window.setTimeout(() => setCopied(false), COPIED_RESET_MS)
    return () => window.clearTimeout(timer)
  }, [copied])

  async function copyCommand(): Promise<void> {
    try {
      await navigator.clipboard.writeText(command)
      setCopied(true)
    } catch {}
  }

  return (
    <div className={styles.card} role="status">
      <span className={styles.icon} aria-hidden>
        <ChannelIconBadge channelName={pending.channelName} size={24} framed={false} />
      </span>
      <span className={styles.text}>
        <span className={styles.title}>{t('channelBinding.pending.title', { command, channel })}</span>
        <span className={styles.detail}>
          {t('channelBinding.pending.expiresIn', { time: formatRemaining(remainingMs) })}
        </span>
      </span>
      <span className={styles.actions}>
        <Button variant="outlineGhost" size="toolbar" onClick={() => { void copyCommand() }}>
          {copied ? t('channelBinding.pending.copied') : t('channelBinding.pending.copy')}
        </Button>
        <Button
          variant="ghost"
          size="toolbar"
          onClick={() => { void useChannelBindingStore.getState().cancelPending(pending.threadId) }}
        >
          {t('channelBinding.pending.cancel')}
        </Button>
      </span>
    </div>
  )
}

function formatRemaining(ms: number): string {
  const totalSeconds = Math.max(0, Math.ceil(ms / 1000))
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return `${minutes}:${seconds.toString().padStart(2, '0')}`
}
