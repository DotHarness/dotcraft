import { useConversationStore } from '@renderer/stores/conversationStore'
import { visualizationFragment } from './content/visualizations'
import { bridgeComposerActions, inputText, onRequest } from './host'
import { noteCopy, quote, showNote } from './notes'
import { lang } from './params'

let viewSequence = 0

export function installConversationHost(): void {
  bridgeComposerActions()

  onRequest('generativeUi/state/read', () => ({ states: [] }))
  onRequest('generativeUi/state/update', () => ({}))

  onRequest('visualization/view/open', (params) => {
    const fragment = typeof params.path === 'string' ? visualizationFragment(params.path, lang) : null
    if (!fragment) throw new Error('This example has no visualization at that path.')
    return { viewHandle: `showcase-view-${++viewSequence}`, fragment, mimeType: 'text/html', widgetState: null }
  })
  onRequest('visualization/view/close', () => ({ closed: true }))
  onRequest('visualization/view/widgetState/update', () => ({}))
  onRequest('visualization/view/message', (params) => {
    showNote(noteCopy.send(quote(String(params.prompt ?? ''))))
    return {}
  })

  onRequest('turn/start', (params) => {
    showNote(noteCopy.send(quote(inputText(params.input))))
    // The example has no reply to stream, so the optimistic turn is dropped once it is echoed.
    setTimeout(() => useConversationStore.setState({ turns: [], turnStatus: 'idle', activeTurnId: null }))
    return { turn: { id: `showcase-sent-${Date.now()}` } }
  })
}
