import { formatCount, formatDuration, formatPercent, parseMetadata, prettyJson, shortHash } from '../core/format.js';
import { esc, facts, mono, num } from '../core/html.js';

const FILTER_TYPES = {
  Response: ['Response', 'ResponseTerminal'],
  Provider: ['ProviderResponseDiagnostic', 'ProviderError'],
  Tool: ['ToolCallStarted', 'ToolCallCompleted', 'ToolInjection', 'DeferredToolLoading', 'ToolPolicyWithheld'],
  Maintenance: ['MaintenanceForkRequest', 'MaintenanceForkResponse', 'ContextCompaction', 'ThreadRollback'],
  TokenUsage: ['TokenUsage', 'PromptCacheDiagnostic', 'SessionMetadata'],
  Cache: ['PromptCacheDiagnostic', 'SessionMetadata', 'ToolInjection', 'SubAgentPrefixDiagnostic'],
  Error: ['Error', 'ProviderError']
};

const CACHE_KINDS = {
  coldStart: 'cold start',
  warmWriteOrNewPrefix: 'new prefix written',
  cacheHitStable: 'cache hit stable',
  cacheReadDrop: 'cache read dropped',
  ttlPossible: 'possibly expired (TTL)',
  promptOrToolsChanged: 'cache key changed',
  likelyServerSide: 'likely server-side'
};

export function matchesFilter(event, filter) {
  if (filter === 'all') return true;
  if (filter === 'Cache' && event.type === 'ToolInjection') return Boolean(event.promptCacheEventKind);
  return (FILTER_TYPES[filter] ?? [filter]).includes(event.type);
}

// Baseline metadata only seeds the drift tracker; there is nothing to show for it.
export function isVisibleEvent(event) {
  if (event.type === 'SessionMetadata') return event.promptCacheEventKind === 'drift' || event.promptCacheEventKind === 'toolExtension';
  return event.type in DESCRIBERS;
}

function toolIcon(name = '') {
  if (/^(Exec|Shell|Bash|RunCommand)/i.test(name)) return 'squareTerminal';
  if (/^(Read|View)/i.test(name)) return 'bookOpen';
  if (/^(Grep|Search|Find)/i.test(name)) return 'search';
  if (/^(List|Glob|Dir)/i.test(name)) return 'folder';
  if (/^(Edit|Write|Patch)/i.test(name)) return 'filePen';
  if (/^(Web|Fetch|Http)/i.test(name)) return 'globe';
  return 'wrench';
}

function oneLine(text, max = 160) {
  const flat = String(text ?? '').replace(/\s+/g, ' ').trim();
  return flat.length > max ? `${flat.slice(0, max)}…` : flat;
}

function argsPreview(raw) {
  const args = parseMetadata(raw);
  const preferred = args.command ?? args.path ?? args.pattern ?? args.url ?? args.query ?? args.notify_id;
  return typeof preferred === 'string' ? preferred : oneLine(raw, 120);
}

function capitalize(text) {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

const count = value => num(typeof value === 'number' ? formatCount(value) : String(value ?? '–'));
const text = value => esc(value ?? '–');
const pair = (before, after) => facts([before, after].map(esc));

function common(event) {
  const items = [];
  if (event.modelId) items.push(['Model', text(event.modelId)]);
  if (event.callId) items.push(['Call', mono(event.callId)]);
  if (event.responseId) items.push(['Response', mono(event.responseId)]);
  if (event.finishReason) items.push(['Finish', text(event.finishReason.replace('_', ' '))]);
  return items;
}

function cacheMiss(event) {
  if ((event.cachedInputTokens ?? 0) > 0) return '';
  return (event.cacheWriteInputTokens ?? 0) > 0 ? 'Cache written' : 'Cache miss';
}

function tokenSummary(event) {
  const input = event.inputTokens ?? 0;
  const cache = cacheMiss(event).toLowerCase() || `${formatPercent((event.cachedInputTokens ?? 0) / input, 0)} cached`;
  return [`${formatCount(event.totalTokens ?? input)} tokens`, `${formatCount(event.outputTokens)} out`, cache];
}

function hashFacts(event) {
  const items = [];
  if (event.previousSystemPromptHash || event.currentSystemPromptHash) {
    items.push(['Prompt hash', pair(shortHash(event.previousSystemPromptHash), shortHash(event.currentSystemPromptHash || event.systemPromptHash))]);
  }
  if (event.previousToolSchemaHash || event.currentToolSchemaHash) {
    items.push(['Tools hash', pair(shortHash(event.previousToolSchemaHash), shortHash(event.currentToolSchemaHash || event.toolSchemaHash))]);
  }
  return items;
}

function cacheUsageFacts(event, md) {
  const items = [];
  if (md.previousCachedInputTokens != null) items.push(['Read before', count(md.previousCachedInputTokens)], ['Read now', count(event.cachedInputTokens ?? md.cachedInputTokens ?? 0)]);
  if (md.cacheReadDropTokens > 0) items.push(['Dropped', facts([formatCount(md.cacheReadDropTokens), formatPercent(md.cacheReadDropRatio || 0)].map(esc))]);
  items.push(['Written', count(md.cacheWriteInputTokens ?? event.cacheWriteInputTokens ?? 0)]);
  if (md.breakpointCount != null) items.push(['Breakpoints', count(md.breakpointCount)]);
  if (md.newSelectedCount != null || md.rememberedSelectedCount != null) items.push(['Breakpoints chosen', facts([`${formatCount(md.newSelectedCount || 0)} new`, `${formatCount(md.rememberedSelectedCount || 0)} remembered`].map(esc))]);
  const changed = [md.promptChanged && 'prompt', md.toolsChanged && 'tools', md.reasoningChanged && 'reasoning'].filter(Boolean);
  items.push(['Changed', text(changed.join(', ') || 'Nothing')]);
  if (md.previousReasoningHash || md.reasoningHash) items.push(['Reasoning hash', pair(shortHash(md.previousReasoningHash), shortHash(md.reasoningHash))]);
  if (md.systemHash || md.toolSchemaHash) items.push(['Prompt hash', mono(shortHash(md.systemHash))], ['Tools hash', mono(shortHash(md.toolSchemaHash))]);
  return items;
}

const DESCRIBERS = {
  AgentInstructions: (event, md) => {
    const sources = Array.isArray(md.sources) ? md.sources : [];
    return {
      icon: 'bookText', label: 'Loaded AGENTS.md instructions',
      detail: [`${sources.length} ${sources.length === 1 ? 'source' : 'sources'}`],
      facts: [['Role', text(md.role || 'user')], ['Fingerprint', mono(shortHash(md.fingerprint))], ...sources.map((source, index) => [index ? '' : 'Sources', mono(source)])],
      content: { text: event.content || 'No AGENTS.md instructions were loaded.', mono: true }
    };
  },
  WorldStateDiagnostic: (event, md) => {
    const changed = md.changedSections ?? [];
    const unchanged = md.unchangedSections ?? [];
    const suppressed = md.suppressedSections ?? [];
    return {
      icon: 'layers',
      label: changed.length ? `World state: sent ${changed.length} ${changed.length === 1 ? 'section' : 'sections'}` : 'World state: nothing new to send',
      detail: [`${unchanged.length} unchanged`, md.baselineSource && md.baselineSource !== 'none' ? `baseline from ${md.baselineSource}` : ''],
      status: suppressed.length ? 'warning' : undefined, statusLabel: 'Some sections were held back',
      facts: [
        ['Status', text(md.status ?? 'unknown')],
        ['Sent', text(changed.join(', ') || 'None')],
        ['Unchanged', text(unchanged.join(', ') || 'None')],
        ...(suppressed.length ? [['Held back', text(suppressed.map(item => `${item.id} (${item.reason})`).join(', '))]] : [])
      ],
      content: event.content ? { text: event.content, mono: true } : undefined
    };
  },
  Request: event => ({ icon: 'messageSquare', label: 'User message', detail: [oneLine(event.content)], facts: common(event), content: { text: event.content ?? '', mono: false } }),
  Thinking: event => ({ icon: 'thinking', label: 'Thought', detail: [oneLine(event.content)], facts: common(event), content: { text: event.content ?? '', mono: false } }),
  Response: event => ({ icon: 'messageSquareText', label: 'Response', detail: [oneLine(event.content)], facts: common(event), content: { text: event.content ?? '', mono: false } }),
  ResponseTerminal: (event, md) => ({
    icon: 'flag', label: md.terminalUpdateSeen ? 'Response ended' : 'Stream ended without a final update',
    detail: [event.finishReason ? event.finishReason.replace('_', ' ') : '', md.hasText ? 'text' : 'no text', md.hasToolCall ? 'tool call' : '', md.hasUsage ? 'usage' : ''],
    status: md.terminalUpdateSeen ? undefined : 'warning', statusLabel: 'No final update',
    facts: [...common(event), ['Request in turn', count(md.requestIndex)]]
  }),
  TokenUsage: event => {
    const input = event.inputTokens ?? 0;
    return {
      icon: 'gauge', label: `Model call ${event.llmCallIndex ?? ''}`.trim(), detail: tokenSummary(event),
      status: cacheMiss(event) ? 'warning' : undefined, statusLabel: cacheMiss(event),
      facts: [
        ['Input', count(input)],
        ['Fresh input', count(event.freshInputTokens)],
        ['Cache hit', `<span class="dash-num">${facts([formatCount(event.cachedInputTokens), formatPercent(input ? (event.cachedInputTokens ?? 0) / input : 0)].map(esc))}</span>`],
        ['Cache write', count(event.cacheWriteInputTokens)],
        ['Output', count(event.outputTokens)],
        ['Total', count(event.totalTokens)],
        ['Request in turn', count(event.requestIndex)],
        ...common(event)
      ]
    };
  },
  ToolCallStarted: event => ({
    icon: toolIcon(event.toolName), label: event.toolName ?? 'Tool', detail: ['Started', argsPreview(event.toolArguments)],
    facts: common(event), content: event.toolArguments ? { text: prettyJson(event.toolArguments), mono: true } : undefined
  }),
  ToolCallCompleted: event => ({
    icon: toolIcon(event.toolName), label: event.toolName ?? 'Tool',
    detail: [`Finished in ${formatDuration(event.durationMs)}`, oneLine(event.toolResult, 100)],
    facts: [['Duration', num(formatDuration(event.durationMs))], ...common(event)],
    content: event.toolResult ? { text: prettyJson(event.toolResult), mono: true } : undefined
  }),
  ToolInjection: event => ({
    icon: 'plug', label: 'Tools added to the next request', detail: [(event.changedToolNames ?? []).join(', ') || oneLine(event.content)],
    status: event.promptCacheEventKind ? 'neutral' : undefined, statusLabel: 'Tool extension',
    facts: [...hashFacts(event), ...common(event)], content: event.content ? { text: event.content, mono: false } : undefined
  }),
  DeferredToolLoading: (event, md) => {
    const tools = md.tools ?? [];
    return {
      icon: 'plug', label: `Activated ${tools.length} deferred ${tools.length === 1 ? 'tool' : 'tools'}`, detail: [tools.map(tool => tool.name).join(', ')],
      facts: [
        ['Strategy', text(md.strategy)], ['Mode', text(md.effectiveMode ?? md.mode)], ['Protocol', text(md.providerProtocol)],
        ['Wire shape', text(md.wireShape)], ['Still deferred', count(md.deferredToolCount)], ['Query', text(md.query)],
        ['Tools', `<span class="dash-tool-names">${tools.map(tool => mono(tool.namespace ? `${tool.name} (${tool.namespace})` : tool.name)).join('')}</span>`]
      ],
      content: event.content ? { text: event.content, mono: false } : undefined
    };
  },
  ToolPolicyWithheld: (event, md) => {
    const tools = md.tools ?? [];
    return {
      icon: 'eyeOff', label: `${tools.length} ${tools.length === 1 ? 'tool' : 'tools'} withheld by policy`, detail: [tools.map(tool => tool.name).join(', ')],
      facts: tools.map(tool => [tool.name, facts([tool.source, (tool.reason ?? '').replace(/_/g, ' ')].map(esc))]),
      content: event.content ? { text: event.content, mono: false } : undefined
    };
  },
  ProviderResponseDiagnostic: (event, md) => {
    if (md.eventType !== 'stream_attempt') {
      const notes = [md.incompleteReason, md.usagePresent ? 'usage' : '', md.metadataExtractionFailed ? 'metadata partial' : ''].filter(Boolean);
      return {
        icon: 'network', label: 'Provider response details', detail: [md.providerProtocol, md.status, ...notes].filter(Boolean),
        status: md.incompleteReason || md.metadataExtractionFailed ? 'warning' : undefined, statusLabel: 'Response incomplete',
        facts: [['Protocol', text(md.providerProtocol)], ['Event', text(md.eventType)], ['Status', text(md.status)], ['Incomplete', text(md.incompleteReason ?? 'No')], ['Usage', md.usagePresent ? 'Recorded' : 'Missing'], ...common(event)]
      };
    }
    const ok = md.outcome === 'succeeded';
    return {
      icon: 'network', label: `Provider attempt ${md.attemptNumber ?? '?'} of ${md.retryLimit ?? '?'}`,
      detail: [md.outcome, md.statusCode != null ? `HTTP ${md.statusCode}` : '', md.durationMs != null ? formatDuration(md.durationMs) : '', md.retryDecision && md.retryDecision !== 'none' ? `retry ${md.retryDecision}` : ''],
      status: ok ? undefined : 'warning', statusLabel: 'Attempt did not succeed',
      facts: [
        ['Outcome', text(md.outcome)], ['Failure', text(md.failureKind ?? 'None')], ['Visible output', md.visibleOutputEmitted ? 'Yes' : 'No'],
        ['Request ID', mono(md.requestId)], ['Session hash', mono(shortHash(md.sessionIdHash))], ['Thread hash', mono(shortHash(md.threadIdHash))], ['Cache key hash', mono(shortHash(md.promptCacheKeyHash))]
      ]
    };
  },
  ProviderError: (event, md) => ({
    icon: 'circleX', label: 'Provider error', detail: [oneLine(event.content)], status: 'error', statusLabel: 'Failed',
    facts: [['Code', mono(md.errorCode)], ['Request in turn', count(md.requestIndex)]], content: { text: event.content ?? '', mono: true, tone: 'error' }
  }),
  Error: event => ({ icon: 'circleX', label: 'Error', detail: [oneLine(event.content)], status: 'error', statusLabel: 'Failed', facts: common(event), content: { text: event.content ?? '', mono: true, tone: 'error' } }),
  ContextCompaction: event => ({ icon: 'archive', label: 'Context compacted', detail: [oneLine(event.content)], facts: common(event), content: { text: event.content ?? '', mono: false } }),
  MaintenanceForkRequest: (event, md) => ({
    icon: 'gitFork', label: `${capitalize(String(md.taskKind ?? 'maintenance'))} request`,
    detail: [`${formatCount(md.snapshotMessageCount)} messages`, String(md.mode ?? 'fork')],
    facts: [['Tools', count(md.toolCount)], ['Snapshot', count(md.snapshotMessageCount)], ['Tail', count(md.extraTailMessageCount)], ['Prompt hash', mono(shortHash(md.baseInstructionsFingerprint))], ['Tools hash', mono(shortHash(md.toolFingerprint))]],
    content: event.content ? { text: event.content, mono: false } : undefined
  }),
  MaintenanceForkResponse: (event, md) => ({
    icon: 'gitFork', label: md.fallbackReason ? `Fell back: ${String(md.fallbackReason).replace(/_/g, ' ')}` : `${capitalize(String(md.taskKind ?? 'maintenance'))} summary`,
    detail: [oneLine(event.content)], status: md.fallbackReason ? 'warning' : undefined, statusLabel: 'Fallback used',
    facts: common(event), content: { text: event.content ?? '', mono: false }
  }),
  ThreadRollback: (event, md) => {
    const turns = md.numTurns ?? md.num_turns ?? 0;
    return {
      icon: 'undo2', label: `Rolled back ${turns} ${turns === 1 ? 'turn' : 'turns'}`,
      detail: [md.remainingTurns != null ? `${md.remainingTurns} turns remain` : ''], facts: [['Source', text(md.source)]]
    };
  },
  PromptCacheDiagnostic: (event, md) => {
    const broke = md.isCacheBreak === true || event.promptCacheBreakDetected === true || event.promptDriftDetected === true;
    const kind = String(md.classification ?? event.promptCacheEventKind ?? 'diagnostic');
    return {
      icon: 'databaseZap', label: `Prompt cache: ${CACHE_KINDS[kind] ?? kind}`,
      detail: [oneLine(event.content) || `Model call ${event.llmCallIndex ?? ''}`.trim()],
      status: broke ? 'warning' : 'neutral', statusLabel: broke ? 'Cache break' : 'Cache diagnostic',
      facts: [['Model call', count(event.llmCallIndex)], ['Request in turn', count(event.requestIndex)], ...cacheUsageFacts(event, md)]
    };
  },
  SessionMetadata: event => {
    const fields = event.promptCacheChangedFields ?? [];
    const tools = event.changedToolNames ?? [];
    const drift = event.promptCacheEventKind === 'drift';
    const changed = fields.includes('prompt') && fields.includes('tools')
      ? 'System prompt and tool schema changed'
      : fields.includes('tools') ? 'Tool schema changed' : 'System prompt changed';
    return {
      icon: 'databaseZap', label: drift ? 'Prompt cache: drift' : 'Prompt cache: tool extension',
      detail: [drift ? changed : `Added ${tools.join(', ')}`],
      status: drift ? 'warning' : 'neutral', statusLabel: drift ? 'Cache key changed' : 'Tool extension',
      facts: [...hashFacts(event), ...(tools.length ? [['Tools added', text(tools.join(', '))]] : [])]
    };
  },
  SubAgentPrefixDiagnostic: (event, md) => {
    const ok = md.status === 'compatible';
    const differs = (md.changedFields ?? [])
      .map(field => (field === 'inputPrefix' ? (md.expectedSharedPrefix ? 'inherited history' : '') : field))
      .filter(Boolean);
    return {
      icon: 'link2', label: ok ? 'Subagent reuses the parent cache' : 'Subagent cache prefix differs from the parent',
      detail: [md.parentInputItemCount == null ? `${formatCount(md.childInputItemCount)} child items` : `${formatCount(md.matchedInputItemCount)} of ${formatCount(md.parentInputItemCount)} parent items matched`],
      status: md.status === 'diverged' ? 'warning' : undefined, statusLabel: 'Diverged',
      facts: [['Status', text(md.status)], ['Child items', count(md.childInputItemCount)], ['First divergence', count(md.divergenceIndex)], ...(differs.length ? [['Differs in', text(differs.join(', '))]] : [])]
    };
  }
};

export function describeEvent(event) {
  const description = DESCRIBERS[event.type](event, parseMetadata(event.metadataJson));
  return { ...description, detail: description.detail.filter(Boolean).map(String) };
}
