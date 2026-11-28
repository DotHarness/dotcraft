import type {
  DesktopPluginHost,
  DesktopPluginLinkedChat,
  DesktopPluginRecentFinishedThreadDigest,
  DesktopPluginRunningThreadDigest
} from '@dotcraft/plugin'

export const CONDUCTOR_KEY = 'conductor'
export const SILENT_REPLY_TOKEN = 'DONT_NOTIFY'
export const AWAY_THRESHOLD_MS = 3 * 60 * 1000
export const CHECKIN_COOLDOWN_MS = 45 * 1000
const STARTUP_SETTLE_MS = 1_500
const STARTUP_WINDOW_MS = 30_000
const RECENT_FINISHED_LOOKBACK_MS = 30 * 60 * 1000

export type ConductorCheckInReason = 'startup' | 'return' | 'enter' | 'resume'

export interface ConductorProactiveSignal {
  noteworthy: boolean
  fingerprint: string
}

export function buildConductorProactiveSignal(
  activeThreads: readonly DesktopPluginRunningThreadDigest[],
  linkedChats: readonly DesktopPluginLinkedChat[],
  conductorThreadId: string,
  recentFinished: readonly DesktopPluginRecentFinishedThreadDigest[] = []
): ConductorProactiveSignal {
  const linkedIds = new Set(linkedChats.map((chat) => chat.threadId))
  const relevantActive = activeThreads
    .filter((thread) => thread.id !== conductorThreadId)
    .sort((a, b) => `${a.projectId}:${a.id}`.localeCompare(`${b.projectId}:${b.id}`))
  const activeIds = new Set(relevantActive.map((thread) => thread.id))
  const sortedLinked = [...linkedChats].sort((a, b) =>
    `${a.projectId}:${a.threadId}`.localeCompare(`${b.projectId}:${b.threadId}`)
  )
  const sortedFinished = recentFinished
    .filter((thread) => thread.id !== conductorThreadId && !activeIds.has(thread.id) && !linkedIds.has(thread.id))
    .sort((a, b) => `${a.projectId}:${a.id}`.localeCompare(`${b.projectId}:${b.id}`))

  const hasUnfollowedActive = relevantActive.some((thread) => !linkedIds.has(thread.id))
  const hasWaiting = relevantActive.some((thread) => thread.activeState === 'needsUser')
    || sortedLinked.some((chat) => chat.state === 'waiting')
  const hasTerminalLinked = sortedLinked.some((chat) => chat.state === 'done' || chat.state === 'failed')
  const hasRecentFinished = sortedFinished.length > 0

  const fingerprint = JSON.stringify({
    active: relevantActive.map((thread) => [
      thread.projectId,
      thread.id,
      thread.activeState,
      thread.waitingFor ?? '',
      linkedIds.has(thread.id)
    ]),
    linked: sortedLinked.map((chat) => [chat.projectId, chat.threadId, chat.state, chat.createdAt]),
    finished: sortedFinished.map((thread) => [thread.projectId, thread.id, thread.lastActiveAt])
  })

  return {
    noteworthy: hasUnfollowedActive || hasWaiting || hasTerminalLinked || hasRecentFinished,
    fingerprint
  }
}

export function buildConductorCheckInPrompt(locale: string, reason: ConductorCheckInReason): string {
  const arrival = reason === 'startup'
    ? 'The person opened DotCraft'
    : reason === 'enter'
      ? 'The person opened Oratorio Conductor'
      : reason === 'resume'
        ? 'The person resumed Oratorio Conductor'
        : 'The person returned to DotCraft after being away'
  return [
    `${arrival} (interface locale: ${locale}).`,
    'Check active chats across running projects with ListThreads (activeOnly: true) and follow any unlinked active chats with FollowThread.',
    'Also check recent unread completed chats in running projects with ListThreads and ReadThread as needed so newly finished work is not missed.',
    `If there is something new worth telling the person, reply concisely in ${locale} in 1-3 lines, listing chats waiting on the person first and summarizing completed, failed, or running updates.`,
    `If nothing new requires the person’s attention or warrants interrupting them, reply with exactly ${SILENT_REPLY_TOKEN}.`
  ].join(' ')
}

export interface ConductorProactiveController {
  start(): () => void
  runCheckIn(reason: ConductorCheckInReason): Promise<boolean>
  reset(): void
}

export function createConductorProactiveController(host: DesktopPluginHost): ConductorProactiveController {
  let blurredAtMs: number | null = null
  let lastCheckInAtMs = 0
  let lastCheckedAtMs = 0
  let lastFingerprint: string | null = null
  let checkingIn = false
  let startupTimer: ReturnType<typeof setTimeout> | null = null
  let startupWindowUntilMs = 0
  let startupChecked = false
  let stopped = false

  const clearStartupTimer = (): void => {
    if (startupTimer != null) {
      clearTimeout(startupTimer)
      startupTimer = null
    }
  }

  const runCheckIn = async (reason: ConductorCheckInReason): Promise<boolean> => {
    if (stopped || checkingIn) return false
    const now = Date.now()
    if (reason !== 'resume' && lastCheckInAtMs > 0 && now - lastCheckInAtMs < CHECKIN_COOLDOWN_MS) {
      return false
    }
    const conductor = host.threads.findOwned(CONDUCTOR_KEY)
    if (!conductor || conductor.paused) return false

    checkingIn = true
    try {
      const sinceMs = lastCheckedAtMs > 0 ? lastCheckedAtMs : now - RECENT_FINISHED_LOOKBACK_MS
      const [linkedChats, running] = await Promise.all([
        host.threads.linked(conductor.threadId),
        host.threads.listRunningThreads({ excludeThreadId: conductor.threadId, finishedSinceMs: sinceMs })
      ])
      const signal = buildConductorProactiveSignal(
        running.activeThreads,
        linkedChats,
        conductor.threadId,
        running.recentFinished
      )
      if (!signal.noteworthy) {
        lastCheckedAtMs = now
        return false
      }
      if (reason !== 'resume' && signal.fingerprint === lastFingerprint) return false

      const result = await host.threads.startBackgroundCheckIn({
        key: CONDUCTOR_KEY,
        prompt: buildConductorCheckInPrompt(host.environment.locale, reason),
        requireExistingTurns: true,
        silentReplyToken: SILENT_REPLY_TOKEN,
        notificationTitle: 'Oratorio'
      })
      if (!result.started) return false
      lastFingerprint = signal.fingerprint
      lastCheckInAtMs = Date.now()
      lastCheckedAtMs = lastCheckInAtMs
      return true
    } catch {
      return false
    } finally {
      checkingIn = false
    }
  }

  const scheduleStartupCheckIn = (): void => {
    if (stopped || startupChecked || (startupWindowUntilMs > 0 && Date.now() > startupWindowUntilMs)) return
    clearStartupTimer()
    startupTimer = setTimeout(() => {
      startupTimer = null
      startupChecked = true
      void runCheckIn('startup')
    }, STARTUP_SETTLE_MS)
  }

  const reset = (): void => {
    clearStartupTimer()
    lastCheckInAtMs = 0
    lastCheckedAtMs = 0
    lastFingerprint = null
  }

  const start = (): (() => void) => {
    stopped = false
    startupWindowUntilMs = Date.now() + STARTUP_WINDOW_MS

    let wasViewingConductor = Boolean(
      host.session.threadId && host.session.threadId === host.threads.findOwned(CONDUCTOR_KEY)?.threadId
    )
    const stopSession = host.session.onChange((session) => {
      const conductorId = host.threads.findOwned(CONDUCTOR_KEY)?.threadId
      const isViewing = Boolean(conductorId && session.threadId === conductorId)
      if (isViewing && !wasViewingConductor) {
        void runCheckIn('enter')
      }
      wasViewingConductor = isViewing
    })

    const stopProjectConnected = host.workspaces.onProjectConnected(() => {
      scheduleStartupCheckIn()
    })

    if (host.threads.findOwned(CONDUCTOR_KEY)) {
      scheduleStartupCheckIn()
    }

    const onBlur = (): void => {
      blurredAtMs = Date.now()
    }
    const onFocus = (): void => {
      const awayMs = blurredAtMs == null ? 0 : Date.now() - blurredAtMs
      blurredAtMs = null
      if (awayMs >= AWAY_THRESHOLD_MS) {
        void runCheckIn('return')
      }
    }
    window.addEventListener('blur', onBlur)
    window.addEventListener('focus', onFocus)

    return () => {
      stopped = true
      clearStartupTimer()
      stopSession()
      stopProjectConnected()
      window.removeEventListener('blur', onBlur)
      window.removeEventListener('focus', onFocus)
    }
  }

  return { start, runCheckIn, reset }
}
