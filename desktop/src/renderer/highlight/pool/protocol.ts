import type { Grammar } from '../prepare'
import type {
  DiffHighlightRequest,
  DiffHighlightResult,
  FileHighlightRequest,
  FileHighlightResult
} from '../types'

export type HighlightTask =
  | { type: 'file'; id: number; request: FileHighlightRequest }
  | { type: 'diff'; id: number; request: DiffHighlightRequest }

export interface InitializeMessage {
  type: 'initialize'
  id: number
  grammars: Grammar[]
}

export interface FileRequestMessage {
  type: 'file'
  id: number
  request: FileHighlightRequest
  grammars: Grammar[]
  lang: string | undefined
}

export interface DiffRequestMessage {
  type: 'diff'
  id: number
  request: DiffHighlightRequest
  grammars: Grammar[]
  deletionLang: string | undefined
  additionLang: string | undefined
}

export type HighlightRequestMessage = FileRequestMessage | DiffRequestMessage
export type WorkerRequestMessage = InitializeMessage | HighlightRequestMessage

export interface InitializedMessage {
  type: 'initialized'
  id: number
}

export interface FileResultMessage {
  type: 'result'
  id: number
  requestType: 'file'
  result: FileHighlightResult
}

export interface DiffResultMessage {
  type: 'result'
  id: number
  requestType: 'diff'
  result: DiffHighlightResult
}

export interface ErrorMessage {
  type: 'error'
  id: number
  error: string
}

export type HighlightResultMessage = FileResultMessage | DiffResultMessage
export type WorkerResponseMessage = InitializedMessage | HighlightResultMessage | ErrorMessage
