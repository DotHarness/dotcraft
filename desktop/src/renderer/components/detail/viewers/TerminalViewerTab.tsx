import { useEffect, useRef } from 'react'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import { WebLinksAddon } from '@xterm/addon-web-links'
import '@xterm/xterm/css/xterm.css'
import { useViewerTabStore } from '../../../stores/viewerTabStore'
import { useConversationStore } from '../../../stores/conversationStore'
import { createTerminalThemeFromDocument } from './viewerTheme'
import { useDocumentThemeMode } from '../../../utils/theme'

interface TerminalViewerTabProps {
  tabId: string
}

function formatExitMessage(code: number | null): string {
  if (typeof code === 'number') return `\r\n[process exited code=${code}]\r\n`
  return '\r\n[process exited]\r\n'
}

export function TerminalViewerTab({ tabId }: TerminalViewerTabProps): JSX.Element {
  const themeMode = useDocumentThemeMode()
  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const hasStarted = useViewerTabStore((s) => {
    const threadId = s.currentThreadId
    if (!threadId) return false
    const tab = s.getThreadState(threadId).tabs.find((item) => item.id === tabId)
    return tab?.kind === 'terminal' ? tab.hasStarted : false
  })
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const containerRef = useRef<HTMLDivElement>(null)
  const terminalRef = useRef<Terminal | null>(null)
  const fitRef = useRef<FitAddon | null>(null)
  const resizeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const startedRef = useRef(false)
  const threadIdRef = useRef<string | null>(currentThreadId)
  const updateTerminalTabRef = useRef(useViewerTabStore.getState().updateTerminalTab)

  useEffect(() => {
    threadIdRef.current = currentThreadId
  }, [currentThreadId])

  useEffect(() => {
    const container = containerRef.current
    if (!container) return

    const term = new Terminal({
      convertEol: true,
      cursorBlink: true,
      scrollback: 5000,
      fontSize: 12,
      fontFamily: 'ui-monospace, "SFMono-Regular", "SF Mono", Menlo, Consolas, "Liberation Mono", monospace',
      theme: createTerminalThemeFromDocument()
    })
    const fitAddon = new FitAddon()
    term.loadAddon(fitAddon)
    term.loadAddon(new WebLinksAddon())
    term.options.disableStdin = true
    term.open(container)
    fitAddon.fit()
    terminalRef.current = term
    fitRef.current = fitAddon

    const flushResize = (): void => {
      if (!fitRef.current || !terminalRef.current) return
      fitRef.current.fit()
      void window.api.workspace.viewer.terminal.resize({
        tabId,
        cols: terminalRef.current.cols,
        rows: terminalRef.current.rows
      })
    }

    const observer = new ResizeObserver(() => {
      if (resizeTimerRef.current != null) {
        clearTimeout(resizeTimerRef.current)
      }
      resizeTimerRef.current = setTimeout(() => {
        flushResize()
      }, 60)
    })
    observer.observe(container)
    flushResize()

    const inputDisposable = term.onData((data) => {
      void window.api.workspace.viewer.terminal.write({ tabId, data })
    })
    const dataUnsub = window.api.workspace.viewer.terminal.onData((event) => {
      if (event.tabId !== tabId) return
      term.write(event.data)
    })
    const exitUnsub = window.api.workspace.viewer.terminal.onExit((event) => {
      if (event.tabId !== tabId) return
      term.options.disableStdin = true
      term.write(formatExitMessage(event.code))
      const threadId = threadIdRef.current
      if (!threadId) return
      updateTerminalTabRef.current(threadId, tabId, {
        exited: { code: event.code, signal: event.signal }
      })
    })

    return () => {
      observer.disconnect()
      inputDisposable.dispose()
      dataUnsub()
      exitUnsub()
      if (resizeTimerRef.current != null) {
        clearTimeout(resizeTimerRef.current)
        resizeTimerRef.current = null
      }
      term.dispose()
      terminalRef.current = null
      fitRef.current = null
      startedRef.current = false
    }
  }, [tabId])

  useEffect(() => {
    if (!terminalRef.current) return
    terminalRef.current.options.theme = createTerminalThemeFromDocument()
  }, [themeMode])

  useEffect(() => {
    const term = terminalRef.current
    const threadId = threadIdRef.current
    if (!term || !workspacePath || !threadId || startedRef.current) return

    startedRef.current = true
    let cancelled = false
    const attachOrCreate = async (): Promise<void> => {
      if (hasStarted) {
        const attached = await window.api.workspace.viewer.terminal.attach({ tabId })
        if (cancelled) return
        if (attached.buffer) {
          term.write(attached.buffer)
        }
        updateTerminalTabRef.current(threadId, tabId, {
          pid: attached.pid,
          shell: attached.shell,
          cwd: attached.cwd,
          exited: attached.exited
        })
        if (attached.exited) {
          term.options.disableStdin = true
        } else {
          term.options.disableStdin = false
          term.focus()
        }
        return
      }

      const created = await window.api.workspace.viewer.terminal.create({
        tabId,
        threadId,
        workspacePath,
        cols: term.cols,
        rows: term.rows
      })
      if (cancelled) return
      updateTerminalTabRef.current(threadId, tabId, {
        hasStarted: true,
        pid: created.pid,
        shell: created.shell,
        cwd: created.cwd,
        exited: undefined
      })
      term.options.disableStdin = false
      term.focus()
    }

    void attachOrCreate().catch(() => {
      if (cancelled) return
      startedRef.current = false
    })
    return () => {
      cancelled = true
    }
  }, [tabId, hasStarted, workspacePath])

  return <div ref={containerRef} style={{ width: '100%', height: '100%', padding: '8px' }} />
}
