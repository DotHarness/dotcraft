import { useCallback, useEffect, useMemo, type KeyboardEvent } from 'react'
import { MessagesSquare } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useAutoScroll } from '../../hooks/useAutoScroll'
import { useConversationStore, type PendingApproval } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { isSideChatRunning, useSideChatStore, type SideChat } from '../../stores/sideChatStore'
import { addToast } from '../../stores/toastStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { reopenSideChat } from '../../utils/sideChat'
import { ApprovalDecisionComposer } from '../conversation/ApprovalDecisionComposer'
import { InputComposer, type InputComposerSubmitPayload } from '../conversation/InputComposer'
import type { ComposerRuntime } from '../conversation/useComposerRunState'
import { collectConversationImages, ConversationImagesContext } from '../conversation/imagePreview/galleryImages'
import { RequestUserInputComposer } from '../conversation/RequestUserInputComposer'
import { ScrollToBottomButton } from '../conversation/ScrollToBottomButton'
import { ConversationColumn } from '../conversation/ConversationColumn'
import { TranscriptTurn } from '../conversation/TranscriptTurn'
import { TranscriptTurnsContext } from '../conversation/useWebCitationSources'
import { Button } from '../ui/Button'
import styles from './SideChat.module.css'

const NO_TURN_COMPLETION = (): null => null

export function SideChatTab({ tabId }: { tabId: string }): JSX.Element | null {
  const sideChatKey = useViewerTabStore((state) => {
    const tab = state.getThreadState(state.currentThreadId ?? '').tabs.find((candidate) => candidate.id === tabId)
    return tab?.kind === 'sideChat' ? tab.sideChatKey : null
  })
  const chat = useSideChatStore((state) => sideChatKey ? state.chats.get(sideChatKey) : undefined)
  if (!chat) return null
  return <SideChatView chat={chat} />
}

function SideChatView({ chat }: { chat: SideChat }): JSX.Element {
  const t = useT()
  const workspacePath = useConversationStore((state) => state.workspacePath)
  const remoteWorkspace = useConversationStore((state) => state.remoteWorkspaceActive)
  const fileWorkspacePath = useThreadStore((state) => state.activeThread?.id === chat.parentThreadId
    ? state.activeThread.effectiveWorkspacePath?.trim() || undefined
    : undefined)
  const focusRequest = useSideChatStore((state) => state.focusRequest?.key === chat.key ? state.focusRequest.nonce : undefined)
  const running = isSideChatRunning(chat)
  const expired = chat.status === 'expired'
  const approval = chat.pendingApprovals[0] ?? null

  useEffect(() => {
    if (chat.unread) useSideChatStore.getState().markRead(chat.key)
  }, [chat.key, chat.unread])

  const approvalRequest = useMemo<PendingApproval | null>(() => approval && {
    ...approval,
    submit: (value) => useSideChatStore.getState().respondToApproval(chat.key, approval.bridgeId, value)
  }, [approval, chat.key])

  const submit = useCallback((payload: InputComposerSubmitPayload) =>
    useSideChatStore.getState().send(chat.key, { inputParts: payload.inputParts, visibleText: payload.visibleText }),
  [chat.key])

  const stop = useCallback((): void => {
    void useSideChatStore.getState().interrupt(chat.key).catch((error: unknown) => {
      addToast(t('composer.stopFailed', { error: error instanceof Error ? error.message : String(error) }), 'error')
    })
  }, [chat.key, t])

  const runtime = useMemo<ComposerRuntime>(() => ({
    turns: chat.turns,
    running,
    interrupting: chat.interruptingTurnId != null,
    onStop: stop
  }), [chat.interruptingTurnId, chat.turns, running, stop])

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    if (event.key !== 'Escape' || !running || approvalRequest || chat.pendingUserInput) return
    event.preventDefault()
    event.stopPropagation()
    stop()
  }

  return (
    <div className={styles.root} data-side-chat={chat.key} onKeyDown={onKeyDown}>
      <SideChatTranscript chat={chat} running={running} />
      {expired ? (
        <div className={styles.expired} role="status">
          <span>{t('sideChat.expired')}</span>
          <Button size="sm" variant="secondary" onClick={() => reopenSideChat(chat, t)}>
            {t('sideChat.startNew')}
          </Button>
        </div>
      ) : approvalRequest ? (
        <ApprovalDecisionComposer key={approvalRequest.bridgeId} request={approvalRequest} showMascot={false} />
      ) : chat.pendingUserInput ? (
        <RequestUserInputComposer
          request={chat.pendingUserInput}
          onResolved={() => useSideChatStore.getState().resolveUserInput(chat.key)}
          showMascot={false}
        />
      ) : (
        <InputComposer
          threadId={chat.key}
          workspacePath={workspacePath}
          fileWorkspacePath={fileWorkspacePath}
          remoteWorkspace={remoteWorkspace}
          minimalChrome
          variant="sideChat"
          placeholder={t('sideChat.placeholder')}
          submitOverride={submit}
          runtime={runtime}
          focusRequest={focusRequest}
          dockPadding="0 12px"
        />
      )}
    </div>
  )
}

function SideChatTranscript({ chat, running }: { chat: SideChat; running: boolean }): JSX.Element {
  const t = useT()
  const { turns, live } = chat
  const lastTurn = turns.at(-1)
  const contentLength = turns.length
    + (lastTurn?.items.length ?? 0)
    + (lastTurn?.items.reduce((total, item) => total + (item.text?.length ?? 0), 0) ?? 0)
    + (live?.text.length ?? 0)
  const { scrollRef, showScrollButton, scrollToBottom } = useAutoScroll(contentLength)
  const remoteWorkspace = useConversationStore((state) => state.remoteWorkspaceActive)
  const getConversationImages = useCallback(
    () => collectConversationImages(turns, { localFiles: !remoteWorkspace }),
    [remoteWorkspace, turns]
  )

  return (
    <div className={styles.viewport}>
      <div ref={scrollRef} className={`${styles.transcript} dc-scrollbar-stable`}>
        {turns.length === 0 ? (
          <div className={styles.empty}>
            <MessagesSquare size={32} strokeWidth={1.5} aria-hidden className={styles.emptyIcon} />
            <p className={styles.emptyTitle}>{t('sideChat.emptyTitle')}</p>
            <p className={styles.emptyBody}>{t('sideChat.emptyBody')}</p>
          </div>
        ) : (
          <ConversationImagesContext.Provider value={getConversationImages}>
            <TranscriptTurnsContext.Provider value={turns}>
              <ConversationColumn className="dc-conversation-column-stack">
                {turns.map((turn, index) => {
                  const isLastTurn = index === turns.length - 1
                  return (
                    <TranscriptTurn
                      key={turn.id}
                      threadId={chat.threadId ?? chat.key}
                      turn={turn}
                      isLastTurn={isLastTurn}
                      running={running && isLastTurn}
                      live={isLastTurn ? live : null}
                      renderTurnCompletion={NO_TURN_COMPLETION}
                    />
                  )
                })}
              </ConversationColumn>
            </TranscriptTurnsContext.Provider>
          </ConversationImagesContext.Provider>
        )}
      </div>
      {showScrollButton && <ScrollToBottomButton onClick={scrollToBottom} working={running} />}
    </div>
  )
}
