import { useCallback, useLayoutEffect, useMemo, useRef, useState } from 'react'

export interface ComposerSendScrollRequest {
  readonly scope: object
}

export function useComposerSendScroll(workspaceKey: string, threadId: string | null, enabled: boolean): {
  scrollRequest: ComposerSendScrollRequest | null
  onOptimisticEcho: () => void
} {
  const scope = useMemo(() => ({}), [workspaceKey, threadId, enabled])
  const activeScope = useRef<object | null>(null)
  const [request, setRequest] = useState<ComposerSendScrollRequest | null>(null)

  useLayoutEffect(() => {
    activeScope.current = scope
    return () => { activeScope.current = null }
  }, [scope])

  const onOptimisticEcho = useCallback(() => {
    if (!enabled || !threadId || activeScope.current !== scope) return
    setRequest({ scope })
  }, [enabled, scope, threadId])

  return { scrollRequest: request?.scope === scope ? request : null, onOptimisticEcho }
}

export function useScrollToLatestOnSend(
  request: ComposerSendScrollRequest | null,
  scrollToBottom: () => void
): void {
  const previousRequest = useRef(request)

  useLayoutEffect(() => {
    if (request === previousRequest.current) return
    previousRequest.current = request
    if (request) scrollToBottom()
  }, [request, scrollToBottom])
}
