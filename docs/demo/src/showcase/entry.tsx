import { StrictMode, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { LocaleProvider } from '@renderer/contexts/LocaleContext'
import { ColorPickerDialogHost } from '@renderer/components/ui/ColorPickerDialog'
import { ConfirmDialogHost } from '@renderer/components/ui/ConfirmDialog'
import { SelectMenuHost } from '@renderer/components/ui/SelectMenu'
import { useConnectionStore } from '@renderer/stores/connectionStore'
import { useConversationStore } from '@renderer/stores/conversationStore'
import { useThreadStore } from '@renderer/stores/threadStore'
import type { Thread } from '@renderer/types/thread'
import { applyTheme } from '@renderer/utils/theme'
import { demoTheme, DEMO_WORKSPACE_PATH } from '../mockApi'
import { AgentBuilderExample } from './AgentBuilderExample'
import { installBuilderHost } from './builderHost'
import { installConversationHost } from './conversationHost'
import { ConversationExample, defaultExample } from './ConversationExample'
import { installShowcaseHost } from './host'
import { feature, initialSelection, SHOWCASE_THREAD_ID, type AnswerMode, type Selection } from './params'
import './showcase.css'

type HostMessage =
  | { type: 'dotcraft-demo:set-theme'; theme: 'dark' | 'light' }
  | { type: 'dotcraft-showcase:show'; example: string; mode: AnswerMode }

function seedStores(): void {
  const startedAt = new Date().toISOString()
  useConnectionStore.getState().setStatus({
    status: 'connected',
    serverInfo: { name: 'DotCraft AppServer', version: 'web-demo', protocolVersion: '1.0' },
    capabilities: { threadManagement: true, generativeUi: true, agentProfileManagement: true }
  })
  useConversationStore.getState().setWorkspacePath(DEMO_WORKSPACE_PATH)
  if (feature === 'agent-builder') return
  useThreadStore.getState().setActiveThreadId(SHOWCASE_THREAD_ID)
  useThreadStore.getState().setActiveThread({
    id: SHOWCASE_THREAD_ID, displayName: null, status: 'active', originChannel: 'dotcraft-desktop', createdAt: startedAt,
    lastActiveAt: startedAt, workspacePath: DEMO_WORKSPACE_PATH, userId: 'local', metadata: {}, turns: []
  } as unknown as Thread)
}

function reportHeight(element: HTMLElement): () => void {
  if (window.parent === window) return () => {}
  let last = 0
  const observer = new ResizeObserver(() => {
    const height = Math.ceil(element.getBoundingClientRect().height)
    if (height === last) return
    last = height
    window.parent.postMessage({ type: 'dotcraft-showcase:height', height }, '*')
  })
  observer.observe(element)
  return () => observer.disconnect()
}

function Showcase(): JSX.Element {
  const rootRef = useRef<HTMLDivElement>(null)
  const [selection, setSelection] = useState<Selection>(() => ({
    ...initialSelection,
    example: initialSelection.example || defaultExample(feature)
  }))

  useEffect(() => {
    const onMessage = (event: MessageEvent): void => {
      if (event.source !== window.parent) return
      const message = event.data as HostMessage | null
      if (message?.type === 'dotcraft-demo:set-theme' && (message.theme === 'dark' || message.theme === 'light')) applyTheme(message.theme)
      if (message?.type === 'dotcraft-showcase:show' && typeof message.example === 'string') {
        setSelection({ example: message.example, mode: message.mode === 'plain' ? 'plain' : 'interactive' })
      }
    }
    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [])

  useLayoutEffect(() => (rootRef.current ? reportHeight(rootRef.current) : undefined), [])

  return (
    <div ref={rootRef} className={feature === 'agent-builder' ? 'showcase showcase--builder' : 'showcase'}>
      {feature === 'agent-builder' ? <AgentBuilderExample /> : <ConversationExample feature={feature} selection={selection} />}
    </div>
  )
}

export function start(): void {
  applyTheme(demoTheme, { syncTitleBarOverlay: false })
  installShowcaseHost()
  if (feature === 'agent-builder') installBuilderHost()
  else installConversationHost()
  seedStores()

  const rootElement = document.getElementById('root')
  if (!rootElement) throw new Error('Root element not found — check showcase.html for <div id="root">')
  createRoot(rootElement).render(
    <StrictMode>
      <LocaleProvider>
        <Showcase />
        <ConfirmDialogHost />
        <ColorPickerDialogHost />
        <SelectMenuHost />
      </LocaleProvider>
    </StrictMode>
  )
}
