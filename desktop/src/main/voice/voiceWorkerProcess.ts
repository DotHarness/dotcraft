import { availableParallelism } from 'os'

import {
  initWhisper,
  type WhisperContext
} from '@fugood/whisper.node'

interface WorkerRequest {
  id: string
  method: 'initialize' | 'transcribe'
  params: Record<string, unknown>
}

let context: WhisperContext | null = null
let initializedModelPath: string | null = null

const parentPort = process.parentPort
if (!parentPort) throw new Error('voice-worker-parent-missing')

parentPort.on('message', (event) => {
  void handleRequest(event.data)
})

async function handleRequest(value: unknown): Promise<void> {
  if (!isWorkerRequest(value)) return
  try {
    switch (value.method) {
      case 'initialize':
        await initialize(value.params)
        respond(value.id, { runtime: 'whisper.cpp', variant: 'default' })
        return
      case 'transcribe':
        respond(value.id, await transcribe(value.params))
        return
    }
  } catch (error) {
    respondError(
      value.id,
      value.method === 'initialize' ? 'worker-unavailable' : normalizeError(error)
    )
  }
}

async function initialize(params: Record<string, unknown>): Promise<void> {
  const protocolVersion = params.protocolVersion
  const modelPath = params.modelPath
  if (protocolVersion !== 1 || typeof modelPath !== 'string' || modelPath.trim().length === 0) {
    throw new WorkerFailure('worker-unavailable')
  }
  if (context && initializedModelPath === modelPath) return

  if (context) await context.release()
  context = null
  initializedModelPath = null
  context = await initWhisper({ filePath: modelPath, useGpu: false }, 'default')
  initializedModelPath = modelPath
}

async function transcribe(
  params: Record<string, unknown>
): Promise<{ transcript: string; language?: string }> {
  if (!context) throw new WorkerFailure('worker-unavailable')
  const wavPath = params.wavPath
  if (typeof wavPath !== 'string' || wavPath.length === 0) {
    throw new WorkerFailure('invalid-audio')
  }

  const operation = context.transcribeFile(wavPath, {
    language: 'auto',
    temperature: 0,
    maxThreads: Math.min(8, Math.max(1, availableParallelism()))
  })
  const result = await operation.promise
  if (result.isAborted) throw new WorkerFailure('cancelled')
  return {
    transcript: result.result.trim(),
    language: result.language
  }
}

function respond(id: string, result: Record<string, unknown>): void {
  parentPort.postMessage({ id, ok: true, result })
}

function respondError(id: string, code: string): void {
  parentPort.postMessage({ id, ok: false, error: { code } })
}

function isWorkerRequest(value: unknown): value is WorkerRequest {
  if (typeof value !== 'object' || value === null) return false
  const request = value as Partial<WorkerRequest>
  return typeof request.id === 'string'
    && (request.method === 'initialize' || request.method === 'transcribe')
    && typeof request.params === 'object'
    && request.params !== null
}

function normalizeError(error: unknown): string {
  if (error instanceof WorkerFailure) return error.code
  return 'transcription-failed'
}

class WorkerFailure extends Error {
  constructor(readonly code: string) {
    super(code)
  }
}
