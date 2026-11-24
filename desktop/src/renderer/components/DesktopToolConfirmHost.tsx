import { useCallback, useEffect, useRef, useState, type JSX } from 'react'
import type { DesktopToolConfirmRequest } from '../../shared/desktopToolConfirm'
import { PluginActivationConfirm } from './plugins/PluginActivationConfirm'
import { ProjectCreationConfirmDialog } from './projects/ProjectCreationConfirmDialog'

export function DesktopToolConfirmHost(): JSX.Element | null {
  const [request, setRequest] = useState<DesktopToolConfirmRequest | null>(null)
  const responding = useRef(false)
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    const offRequest = window.api.workspace.onDesktopToolConfirmRequest((next) => {
      responding.current = false
      setSubmitting(false)
      setRequest(next)
    })
    const offDismissed = window.api.workspace.onDesktopToolConfirmDismissed(({ requestId }) => {
      setRequest((current) => {
        if (current?.requestId !== requestId) return current
        responding.current = false
        setSubmitting(false)
        return null
      })
    })
    return () => {
      offRequest()
      offDismissed()
    }
  }, [])

  const respond = useCallback(async (confirmed: boolean): Promise<void> => {
    if (!request || responding.current) return
    responding.current = true
    setSubmitting(true)
    try {
      await window.api.workspace.respondDesktopToolConfirm({
        requestId: request.requestId,
        confirmed
      })
      setRequest((current) => (current?.requestId === request.requestId ? null : current))
    } catch (error) {
      console.warn('Failed to respond to a Desktop tool confirmation', error)
    } finally {
      responding.current = false
      setSubmitting(false)
    }
  }, [request])

  const onRespond = useCallback((confirmed: boolean): void => { void respond(confirmed) }, [respond])
  if (!request) return null
  return request.kind === 'pluginActivation'
    ? <PluginActivationConfirm key={request.requestId} request={request} submitting={submitting} onRespond={onRespond} />
    : <ProjectCreationConfirmDialog key={request.requestId} request={request} submitting={submitting} onRespond={onRespond} />
}
