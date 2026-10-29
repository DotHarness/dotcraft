import { useEffect, useRef, useState } from 'react'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { updateThreadReadState } from '../utils/threadReadState'

export function useActiveThreadReadReceipt(): void {
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const readState = useThreadStore((s) => {
    const id = s.activeThreadId
    if (!id) return undefined
    return s.threadList.find((thread) => thread.id === id)?.readState
      ?? (s.activeThread?.id === id ? s.activeThread.readState : undefined)
  })
  const conversationVisible = useUIStore((s) => s.activeMainView === 'conversation')
  const [focused, setFocused] = useState(() => document.hasFocus())
  const openedThreadIdRef = useRef<string | null>(null)
  const sentKeyRef = useRef<string | null>(null)

  useEffect(() => {
    const onFocus = (): void => setFocused(true)
    const onBlur = (): void => setFocused(false)
    window.addEventListener('focus', onFocus)
    window.addEventListener('blur', onBlur)
    return () => {
      window.removeEventListener('focus', onFocus)
      window.removeEventListener('blur', onBlur)
    }
  }, [])

  useEffect(() => {
    openedThreadIdRef.current = activeThreadId
  }, [activeThreadId])

  useEffect(() => {
    if (!activeThreadId || !readState || !focused || !conversationVisible) return
    const justOpened = openedThreadIdRef.current === activeThreadId
    openedThreadIdRef.current = null
    const position = readState.firstUnread
    if (!position || (position.type === 'threadStart' && !justOpened)) return
    const key = `${activeThreadId}\u0000${readState.revision}`
    if (sentKeyRef.current === key) return
    sentKeyRef.current = key
    void updateThreadReadState(activeThreadId, readState, 'read')
  }, [activeThreadId, readState, focused, conversationVisible])
}
