import { parseMetadata } from '../core/format.js';

function rollbackKey(event) {
  const md = parseMetadata(event.metadataJson);
  const time = Date.parse(event.timestamp || '');
  const timestamp = Number.isFinite(time) ? new Date(time).toISOString() : String(event.timestamp || '');
  return `${md.threadId || event.sessionKey || ''}|${timestamp}|${md.numTurns ?? md.num_turns ?? ''}`;
}

function fromOperation(operation, sessionKey) {
  return {
    id: operation.id,
    type: 'ThreadRollback',
    sessionKey,
    timestamp: operation.timestamp,
    metadataJson: JSON.stringify({
      threadId: operation.threadId,
      numTurns: operation.numTurns,
      source: operation.source || 'rollout',
      operationId: operation.id
    })
  };
}

// Rollout records rollbacks the trace may also have; both describe the same rollback, so one is kept.
export function rollbackEvents(events, operations, sessionKey) {
  const rollbacks = operations.filter(operation => operation?.type === 'rollback');
  if (!rollbacks.length) return events;
  const seen = new Set(events.filter(event => event.type === 'ThreadRollback').map(rollbackKey));
  const output = [...events];
  for (const operation of rollbacks) {
    const event = fromOperation(operation, sessionKey);
    const key = rollbackKey(event);
    if (seen.has(key)) continue;
    seen.add(key);
    output.push(event);
  }
  return output.sort((a, b) => {
    const delta = Date.parse(a.timestamp || 0) - Date.parse(b.timestamp || 0);
    return Number.isFinite(delta) && delta !== 0 ? delta : String(a.id || '').localeCompare(String(b.id || ''));
  });
}
