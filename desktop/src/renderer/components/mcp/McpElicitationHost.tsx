import { useEffect, useRef, useState } from 'react'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { dismissThreadElicitation, useThreadElicitationStore } from '../../stores/threadServerRequests'
import { McpElicitationDialog, type McpElicitationRequest, type McpElicitationResponse } from './McpElicitationDialog'

export function McpElicitationHost(): JSX.Element | null {
  const threadId = useThreadStore(state => state.activeThreadId)
  const connectionId = useConnectionStore(state => state.connectionId)
  const request = useThreadElicitationStore(state => state.requests.find(candidate =>
    candidate.connectionId === connectionId && candidate.threadId === threadId) ?? null)

  useEffect(() => window.api.appServer.onServerRequestSettled(({ bridgeId }) => {
    dismissThreadElicitation(bridgeId)
  }), [])

  return request ? <McpElicitationPrompt key={request.bridgeId} request={request} /> : null
}

function McpElicitationPrompt({ request }: { request: McpElicitationRequest }): JSX.Element {
  const responding = useRef(false)
  const [submitting, setSubmitting] = useState(false)
  const respond = async (response: McpElicitationResponse): Promise<void> => {
    if (responding.current) return
    responding.current = true
    setSubmitting(true)
    try {
      await window.api.appServer.sendServerResponse(request.bridgeId, response)
      dismissThreadElicitation(request.bridgeId)
    } catch (error) {
      console.warn('Failed to answer MCP input request', error)
      responding.current = false
      setSubmitting(false)
    }
  }

  return <McpElicitationDialog request={request} submitting={submitting} onRespond={response => { void respond(response) }} />
}
