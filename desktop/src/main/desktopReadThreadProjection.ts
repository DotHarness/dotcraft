type JsonObject = Record<string, unknown>

export interface HistoryPreviewOptions {
  includeOutputs: boolean
  maxCharsPerItem: number
  maxOutputCharsPerItem: number
}

export function projectReadThreadHeader(thread: JsonObject): JsonObject {
  const inputs = Array.isArray(thread.queuedInputs) ? thread.queuedInputs : []
  return {
    id: thread.id ?? '',
    displayName: thread.displayName ?? null,
    status: thread.status ?? 'unknown',
    originChannel: thread.originChannel ?? null,
    createdAt: thread.createdAt ?? null,
    lastActiveAt: thread.lastActiveAt ?? null,
    runtime: thread.runtime ?? null,
    goal: thread.goal ?? null,
    queuedInputCount: inputs.length,
    queuedInputs: inputs.slice(0, 10).map(input => {
      if (!isRecord(input)) return { id: 'unknown', status: 'queued' }
      const sender = isRecord(input.sender) ? input.sender : null
      return {
        id: input.id ?? '',
        status: input.status ?? 'queued',
        displayText: preview(typeof input.displayText === 'string' ? input.displayText : '', 500),
        createdAt: input.createdAt ?? null,
        sender: sender ? {
          displayName: sender.displayName ?? null,
          userId: sender.userId ?? null,
          channelName: sender.channelName ?? null
        } : null,
        triggerLabel: input.triggerLabel ?? null,
        readyAfterTurnId: input.readyAfterTurnId ?? null
      }
    })
  }
}

export function projectReadThreadTurn(
  turn: JsonObject,
  itemsView: 'summary' | 'none',
  options: HistoryPreviewOptions
): JsonObject {
  const items = itemsView === 'summary' && Array.isArray(turn.items) ? turn.items.filter(isRecord) : []
  const itemCount = typeof turn.itemCount === 'number' ? turn.itemCount : null
  const result = {
    ...pick(turn, ['id', 'status', 'startedAt', 'createdAt', 'completedAt', 'durationMs']),
    itemsView,
    ...(itemCount === null ? {} : { itemCount, itemsTruncated: itemCount > items.length }),
    items: items.map(item => projectReadThreadItem(item, options))
  }
  if (turn.error !== undefined) {
    const error = turn.error
    const text = typeof error === 'string' ? error : JSON.stringify(error)
    Object.assign(result, text.length > options.maxCharsPerItem
      ? { errorPreview: textPreview(text, options.maxCharsPerItem) }
      : { error })
  }
  return result
}

const IDENTITY_FIELDS = new Set([
  'namespace', 'toolName', 'server', 'tool', 'callId', 'requestId', 'senderId', 'channelName',
  'deliveryMode', 'phase', 'status', 'source', 'sessionId', 'errorCode', 'code', 'kind', 'mode',
  'sourceThreadId', 'approvalType', 'decision', 'triggerKind', 'savedHostId', 'savedWorkspaceId'
])

export function projectReadThreadItem(item: JsonObject, options?: HistoryPreviewOptions): JsonObject {
  const payload = isRecord(item.payload) ? item.payload : {}
  const type = item.type ?? item.payloadKind ?? 'unknown'
  const result: JsonObject = { id: item.id ?? '', type, status: item.status ?? 'unknown' }
  let remainingChars = options?.maxCharsPerItem ?? Infinity
  let remainingOutputChars = options?.maxOutputCharsPerItem ?? Infinity
  const truncation = { preview: false, outputs: false }

  const write = (field: string, value: unknown, output = false,
    upstreamTruncated = false, originalChars?: number): void => {
    if (value === undefined) return
    const text = typeof value === 'string' ? value : JSON.stringify(value)
    const remaining = output ? remainingOutputChars : remainingChars
    const length = Math.min(text.length, remaining)
    if (output) remainingOutputChars -= length
    else remainingChars -= length
    if (options && (text.length > remaining || upstreamTruncated)) {
      const bounded = textPreview(text, remaining, upstreamTruncated, originalChars)
      if (!output && field === 'text' && typeof value === 'string') {
        result.text = bounded.text
        result.textTruncated = true
        result.textOriginalChars = bounded.originalChars
      } else {
        result[`${field}Preview`] = bounded
      }
      if (output) truncation.outputs = true
      else truncation.preview = true
    } else {
      result[field] = value
    }
  }
  const copy = (fields: string[]): void => {
    for (const field of fields) {
      const value = payload[field]
      if ((IDENTITY_FIELDS.has(field) && typeof value === 'string') || value === null || typeof value === 'number' || typeof value === 'boolean') {
        result[field] = value
      } else write(field, value)
    }
  }
  const output = (field: string, value: unknown, upstreamTruncated = false, originalChars?: number): void => {
    if (options && !options.includeOutputs) return
    write(field, value, true, upstreamTruncated, originalChars)
  }
  const toolOutputs = (): void => {
    const content = [payload.contentItems, payload.modelContentItems, payload.content]
      .find(value => Array.isArray(value) && value.length > 0)
    if (Array.isArray(content)) output('content', content.filter(isRecord).map(projectToolContent))
    if (payload.structuredContent != null) output('structuredContent', payload.structuredContent)
  }

  switch (type) {
    case 'userMessage': {
      if (typeof payload.text === 'string' && payload.text.length > 0) write('text', payload.text)
      const native = Array.isArray(payload.nativeInputParts) ? payload.nativeInputParts.filter(isRecord) : []
      const materialized = Array.isArray(payload.materializedInputParts) ? payload.materializedInputParts.filter(isRecord) : []
      const parts = materialized.length > 0 ? materialized : native
      const seen = new Set(typeof payload.text === 'string' && payload.text ? [payload.text] : [])
      const texts = parts.filter(part => part.type === 'text').map(part => part.text)
      const bodyOnly = payload.text && (texts.join('') === payload.text || texts.join('\n') === payload.text)
      const content = parts.filter(part => {
        if (part.type !== 'text' || typeof part.text !== 'string') return true
        if (bodyOnly || seen.has(part.text)) return false
        seen.add(part.text)
        return true
      }).map(projectInputPart)
      if (content.length > 0) write('content', content)
      if (Array.isArray(payload.images)) {
        write('images', payload.images.filter(isRecord).map(image => mediaReference(image)))
      }
      copy(['deliveryMode', 'senderId', 'senderName', 'channelName', 'triggerKind', 'triggerLabel'])
      break
    }
    case 'agentMessage':
    case 'plan':
      copy(['text', 'phase', 'deliveryMode'])
      break
    case 'reasoningContent':
      output('text', payload.text)
      break
    case 'commandExecution':
      copy(['command', 'workingDirectory', 'source', 'sessionId', 'outputPath', 'backgroundReason',
        'callId', 'status', 'exitCode', 'durationMs', 'originalOutputChars', 'truncated', 'outputStreamTruncated'])
      output('output', payload.aggregatedOutput, payload.truncated === true,
        typeof payload.originalOutputChars === 'number' ? payload.originalOutputChars : undefined)
      if (options && !options.includeOutputs && typeof payload.aggregatedOutput === 'string') {
        result.outputChars = payload.aggregatedOutput.length
      }
      break
    case 'toolExecution':
      copy(['callId', 'toolName', 'status', 'success', 'durationMs', 'errorMessage'])
      output('resultPreview', payload.resultPreview)
      break
    case 'toolCall':
      copy(['namespace', 'toolName', 'callId', 'arguments'])
      break
    case 'dynamicToolCall':
    case 'mcpToolCall':
      copy(['namespace', 'toolName', 'server', 'tool', 'callId', 'arguments', 'status',
        'durationMs', 'success', 'isError', 'errorCode', 'errorMessage'])
      toolOutputs()
      break
    case 'toolResult':
      copy(['namespace', 'toolName', 'callId', 'success', 'durationMs', 'errorCode', 'errorMessage'])
      output('result', payload.result)
      toolOutputs()
      if (options && !options.includeOutputs && typeof payload.result === 'string') result.resultChars = payload.result.length
      break
    case 'approvalRequest':
      copy(['approvalType', 'operation', 'target', 'requestId', 'scopeKey', 'reason'])
      break
    case 'approvalResponse':
      copy(['requestId', 'approved', 'decision'])
      break
    case 'userInputRequest': {
      copy(['requestId'])
      const questions = Array.isArray(payload.questions) ? payload.questions.filter(isRecord) : []
      result.questionCount = questions.length
      write('questions', questions.map(question => ({
        ...pick(question, ['id', 'header', 'question']),
        optionCount: Array.isArray(question.options) ? question.options.length : 0
      })))
      break
    }
    case 'userInputResponse':
      copy(['requestId'])
      break
    case 'systemNotice':
      copy(['kind', 'trigger', 'mode', 'sourceThreadId', 'tokensBefore', 'tokensAfter',
        'percentLeftAfter', 'clearedToolResults'])
      break
    case 'error':
      copy(['code', 'fatal', 'message'])
      break
    case 'imageGeneration':
      copy(['callId', 'status', 'revisedPrompt', 'savedPath', 'mediaType', 'errorCode', 'errorMessage',
        'saveStatus', 'saveErrorCode', 'savedHostId', 'savedWorkspaceId'])
      break
    case 'webSearch':
      copy(['callId', 'status', 'query', 'action', 'errorMessage'])
      output('output', payload.output)
      break
    default:
      copy(['text', 'message', 'errorMessage'])
      output('output', payload.aggregatedOutput ?? payload.output ?? payload.result ?? item.output)
      break
  }
  result.truncation = truncation
  return result
}

function projectInputPart(part: JsonObject): JsonObject {
  const result: JsonObject = { type: part.type ?? 'unknown' }
  switch (part.type) {
    case 'text':
      return { ...result, ...pick(part, ['text']) }
    case 'commandRef':
      return { ...result, ...pick(part, ['name', 'argsText', 'rawText']) }
    case 'skillRef':
      return { ...result, ...pick(part, ['name']) }
    case 'contextRef':
      return isRecord(part.context) ? { ...result, context: projectContext(part.context) } : result
    default:
      return { ...result, ...mediaReference(part) }
  }
}

function projectContext(context: JsonObject): JsonObject {
  const result = withoutInlineMediaUrls(pick(context, ['id', 'kind', 'path', 'fileName', 'preview', 'characterCount',
    'threadId', 'turnId', 'itemId', 'selectedText', 'comment', 'url', 'title', 'selectionKind', 'text']))
  if (isRecord(context.image)) result.image = mediaReference(context.image)
  return result
}

function projectToolContent(content: JsonObject): JsonObject {
  if (content.type === 'text') return pick(content, ['type', 'text'])
  if (content.type === 'resource' && isRecord(content.resource)) {
    return { type: 'resource', resource: withoutInlineMediaUrls(pick(content.resource, ['uri', 'mimeType', 'text'])) }
  }
  return { ...pick(content, ['type']), ...mediaReference(content) }
}

function mediaReference(source: JsonObject): JsonObject {
  return withoutInlineMediaUrls(pick(source, ['name', 'path', 'tempPath', 'displayPath', 'url', 'uri', 'fileName', 'mimeType', 'mediaType']))
}

function withoutInlineMediaUrls(reference: JsonObject): JsonObject {
  for (const field of ['url', 'uri']) {
    if (typeof reference[field] === 'string' && /^data:/i.test(reference[field])) delete reference[field]
  }
  return reference
}

function textPreview(text: string, maxChars: number, upstreamTruncated = false, originalChars?: number): JsonObject {
  let end = Math.min(text.length, maxChars)
  if (end > 0 && end < text.length && /[\uD800-\uDBFF]/.test(text[end - 1])) end--
  return {
    text: text.slice(0, end),
    truncated: upstreamTruncated || end < text.length,
    originalChars: originalChars ?? text.length
  }
}

function pick(source: JsonObject, fields: string[]): JsonObject {
  const result: JsonObject = {}
  for (const field of fields) if (source[field] !== undefined) result[field] = source[field]
  return result
}

function isRecord(value: unknown): value is JsonObject {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function preview(text: string, limit: number): string {
  return text.length > limit ? `${text.slice(0, limit - 3)}...` : text
}
