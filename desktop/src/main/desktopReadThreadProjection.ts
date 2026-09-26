type JsonObject = Record<string, unknown>

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
  items: JsonObject[],
  includeOutputs: boolean,
  maxOutputChars: number
): JsonObject {
  return {
    ...pick(turn, ['id', 'status', 'error', 'startedAt', 'completedAt', 'durationMs']),
    items: items.map(item => projectItem(item, includeOutputs, maxOutputChars))
  }
}

function projectItem(item: JsonObject, includeOutputs: boolean, maxChars: number): JsonObject {
  const payload = isRecord(item.payload) ? item.payload : {}
  const type = item.type ?? item.payloadKind ?? 'unknown'
  const result: JsonObject = { id: item.id ?? '', type, status: item.status ?? 'unknown' }
  const copy = (fields: string[]) => Object.assign(result, pick(payload, fields))
  const output = (field: string, value: unknown) => {
    if (includeOutputs && typeof value === 'string') result[field] = boundedOutput(value, maxChars)
  }
  switch (type) {
    case 'userMessage': {
      copy(['text', 'deliveryMode', 'senderId', 'senderName', 'channelName', 'triggerKind', 'triggerLabel'])
      const native = Array.isArray(payload.nativeInputParts) ? payload.nativeInputParts : []
      const materialized = Array.isArray(payload.materializedInputParts) ? payload.materializedInputParts : []
      const parts = native.length > 0 ? native : materialized
      if (parts.length > 0) result.content = parts.filter(isRecord).map(projectInputPart)
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
      if (includeOutputs && typeof payload.aggregatedOutput === 'string') {
        result.output = boundedOutput(payload.aggregatedOutput, maxChars, payload.truncated === true,
          typeof payload.originalOutputChars === 'number' ? payload.originalOutputChars : undefined)
      } else if (typeof payload.aggregatedOutput === 'string') {
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
      if (includeOutputs) addToolOutputs(result, payload, maxChars)
      break
    case 'toolResult':
      copy(['namespace', 'toolName', 'callId', 'success', 'durationMs', 'errorCode', 'errorMessage'])
      if (includeOutputs) {
        output('result', payload.result)
        addToolOutputs(result, payload, maxChars)
      } else if (typeof payload.result === 'string') {
        result.resultChars = payload.result.length
      }
      break
    case 'approvalRequest':
      copy(['approvalType', 'operation', 'target', 'requestId', 'scopeKey', 'reason'])
      break
    case 'approvalResponse':
      copy(['requestId', 'approved', 'decision'])
      break
    case 'userInputRequest': {
      copy(['requestId'])
      const questions = Array.isArray(payload.questions) ? payload.questions : []
      result.questionCount = questions.length
      result.questions = questions.filter(isRecord).map(question => ({
        ...pick(question, ['id', 'header', 'question']),
        optionCount: Array.isArray(question.options) ? question.options.length : 0
      }))
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
    case 'sleep':
      copy(['durationMs'])
      break
    default:
      copy(['text', 'message', 'errorMessage'])
      output('output', payload.aggregatedOutput ?? payload.output ?? payload.result ?? item.output)
      break
  }
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
    default:
      Object.assign(result, pick(part, ['name', 'path', 'displayPath', 'url', 'fileName', 'mimeType']))
      if (typeof result.url === 'string' && result.url.startsWith('data:')) delete result.url
      return result
  }
}

function addToolOutputs(result: JsonObject, payload: JsonObject, maxChars: number): void {
  const content = [payload.contentItems, payload.modelContentItems, payload.content]
    .find(value => Array.isArray(value) && value.length > 0) as unknown[] | undefined
  const texts = (content ?? []).filter(isRecord).flatMap(item => typeof item.text === 'string' ? [item.text] : [])
  if (texts.length > 0) result.content = boundedOutput(texts.join('\n'), maxChars)
  if (payload.structuredContent != null) {
    result.structuredContent = boundedOutput(JSON.stringify(payload.structuredContent), maxChars)
  }
}

function boundedOutput(text: string, maxChars: number, upstreamTruncated = false, originalChars?: number): JsonObject {
  const truncated = upstreamTruncated || text.length > maxChars
  return {
    text: text.slice(0, maxChars),
    truncated,
    ...(truncated && (!upstreamTruncated || originalChars != null)
      ? { originalChars: originalChars ?? text.length }
      : {})
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
