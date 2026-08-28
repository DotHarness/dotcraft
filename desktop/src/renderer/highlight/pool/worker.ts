/// <reference lib="webworker" />
// Grammar registrations arrive with the request that needs them, so this bundle
// does not pull in the catalogue.
import { createHighlighter, installGrammars, type Highlighter } from '../core'
import { executeDiff, executeFile } from '../execute'
import type { WorkerRequestMessage, WorkerResponseMessage } from './protocol'

let instance: Highlighter | undefined

function highlighter(): Highlighter {
  instance ??= createHighlighter()
  return instance
}

function post(message: WorkerResponseMessage): void {
  ;(self as unknown as DedicatedWorkerGlobalScope).postMessage(message)
}

function handle(message: WorkerRequestMessage): void {
  const active = highlighter()
  installGrammars(active, message.grammars)

  switch (message.type) {
    case 'initialize':
      post({ type: 'initialized', id: message.id })
      return
    case 'file':
      post({
        type: 'result',
        id: message.id,
        requestType: 'file',
        result: executeFile(active.core, message.request, message.lang)
      })
      return
    case 'diff':
      post({
        type: 'result',
        id: message.id,
        requestType: 'diff',
        result: executeDiff(
          active.core,
          message.request,
          message.deletionLang,
          message.additionLang
        )
      })
  }
}

self.addEventListener('message', (event: MessageEvent<WorkerRequestMessage>) => {
  const message = event.data
  try {
    handle(message)
  } catch (error: unknown) {
    post({
      type: 'error',
      id: message.id,
      error: error instanceof Error ? error.message : String(error)
    })
  }
})
