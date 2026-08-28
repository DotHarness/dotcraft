import { useEffect, useRef, useState } from 'react'
import { X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { wireTurnToConversationTurn } from '../../types/conversation'
import type { ConversationTurn } from '../../types/conversation'
import { useCronStore, type CronJobWire } from '../../stores/cronStore'
import { AgentResponseBlock } from '../conversation/AgentResponseBlock'
import { IconButton } from '../ui/IconButton'
import { readThreadHistoryHead } from '../../utils/threadHistory'

export function CronReviewPanel(): JSX.Element {
  const t = useT()
  const selectedCronJobId = useCronStore((s) => s.selectedCronJobId)
  const jobs = useCronStore((s) => s.jobs)
  const selectCronJob = useCronStore((s) => s.selectCronJob)

  const [turns, setTurns] = useState<ConversationTurn[]>([])
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)

  const job: CronJobWire | undefined = selectedCronJobId
    ? jobs.find((j) => j.id === selectedCronJobId)
    : undefined

  const threadId = job?.state.lastThreadId

  useEffect(() => {
    if (!selectedCronJobId || !threadId) {
      setTurns([])
      setLoadError(null)
      setLoading(false)
      return
    }

    let cancelled = false
    setLoading(true)
    setLoadError(null)

    void readThreadHistoryHead(
      (method, params) => window.api.appServer.sendRequest(method, params),
      threadId
    )
      .then((res) => {
        if (cancelled) return
        const rawTurns = res.thread.turns ?? []
        const mapped = rawTurns.map((turn) =>
          wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)
        )
        setTurns(mapped)
        setLoading(false)
      })
      .catch((e: unknown) => {
        if (cancelled) return
        setLoadError(e instanceof Error ? e.message : String(e))
        setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [selectedCronJobId, threadId])

  useEffect(() => {
    function onKey(e: KeyboardEvent): void {
      if (e.key === 'Escape') selectCronJob(null)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [selectCronJob])

  if (!selectedCronJobId || !job) return <></>

  return (
    <div
      style={{
        width: '100%',
        minWidth: 0,
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        borderLeft: '1px solid var(--border-default)',
        backgroundColor: 'var(--bg-primary)',
        flexShrink: 0
      }}
    >
      <div
        style={{
          padding: '12px 14px',
          borderBottom: '1px solid var(--border-default)',
          display: 'flex',
          alignItems: 'flex-start',
          justifyContent: 'space-between',
          gap: '8px',
          flexShrink: 0
        }}
      >
        <div style={{ minWidth: 0, flex: 1 }}>
          <div
            style={{
              fontSize: '14px',
              fontWeight: 600,
              color: 'var(--text-primary)',
              lineHeight: 1.3,
              wordBreak: 'break-word'
            }}
          >
            {job.name}
          </div>
          <div style={{ fontSize: '12px', color: 'var(--text-tertiary)', marginTop: '6px' }}>
            {t('cron.review.cronJobLastRun')}{' '}
            {job.state.lastRunAtMs != null
              ? new Date(job.state.lastRunAtMs).toLocaleString()
              : '—'}
          </div>
        </div>
        <IconButton
          icon={<X size={16} aria-hidden />}
          label={t('auto.review.panelCloseAria')}
          tooltipLabel={t('auto.review.panelCloseAria')}
          tooltipPlacement="bottom"
          size={28}
          radius={6}
          onClick={() => selectCronJob(null)}
        />
      </div>

      {loading && (
        <div style={{ padding: '16px', fontSize: '13px', color: 'var(--text-tertiary)' }}>
          {t('threadList.loading')}
        </div>
      )}

      {loadError && !loading && (
        <div style={{ padding: '16px', fontSize: '13px', color: 'var(--error)' }}>{loadError}</div>
      )}

      {!loading && !loadError && !threadId && (
        <div style={{ padding: '16px', fontSize: '13px', color: 'var(--text-secondary)' }}>
          {t('cron.review.noExecutionYet')}
        </div>
      )}

      {!loading && !loadError && threadId && turns.length === 0 && (
        <div style={{ padding: '16px', fontSize: '13px', color: 'var(--text-secondary)' }}>
          {t('cron.review.noTurnsInThread')}
        </div>
      )}

      <div
        ref={scrollRef}
        className="dc-scrollbar-stable"
        style={{
          flex: 1,
          overflowY: 'auto',
          padding: '12px 14px',
          minHeight: 0
        }}
      >
        {turns.map((turn, idx) => (
          <div key={turn.id} style={{ marginBottom: '16px' }}>
            <AgentResponseBlock
              turn={turn}
              streamingMessage=""
              streamingReasoning=""
              isRunning={false}
              isActiveTurn={false}
              shellRuntimeScope="none"
              isLastTurn={idx === turns.length - 1}
            />
          </div>
        ))}
      </div>
    </div>
  )
}
