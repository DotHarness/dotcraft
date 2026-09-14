import { browserDocumentation, browserTopic } from './browser-documentation.mjs'
import { Buffer } from 'node:buffer'
import { mkdir, mkdtemp, readdir, writeFile } from 'node:fs/promises'
import { tmpdir, platform } from 'node:os'
import path from 'node:path'

const FRAME_HEADER_BYTES = 4
const MAX_RESULT_BYTES = 1024 * 1024
const REQUEST_META_KEY = 'x-dotcraft-turn-metadata'
const PIPE_DIR_NAME = 'dotcraft-browser-use'
const WINDOWS_PIPE_PREFIX = '\\\\.\\pipe\\dotcraft-browser-use'
const READONLY_EVALUATE_ERROR = 'ReadonlyEvaluateViolation'
const WEBMCP_UNAVAILABLE_ERROR = 'Capability is not available: webmcp'
const WEBMCP_CAPABILITY = {
  id: 'webmcp',
  description: 'List and invoke WebMCP tools explicitly exposed by the current page through navigator.modelContext.',
  docs: 'docs/capabilities/tab/webmcp.md'
}
const READONLY_EVALUATE_DENY_PATTERNS = [
  { pattern: /\b(?:window\.)?(?:scrollTo|scrollBy|open|close|stop|print)\s*\(/, label: 'window mutation or viewport side effect' },
  { pattern: /\b(?:window\.)?location(?:\s*=|\.[A-Za-z_$][\w$]*\s*=|\.(?:assign|replace|reload)\s*\()/, label: 'navigation mutation' },
  { pattern: /\bhistory\.(?:pushState|replaceState|go|back|forward)\s*\(/, label: 'history mutation' },
  { pattern: /\bdocument\.(?:write|writeln|open|close)\s*\(/, label: 'document mutation' },
  { pattern: /\bdocument\.cookie\s*=/, label: 'cookie write' },
  { pattern: /\b(?:localStorage|sessionStorage)\.(?:setItem|removeItem|clear)\s*\(/, label: 'storage mutation' },
  { pattern: /\bnavigator\.sendBeacon\s*\(/, label: 'network side effect' },
  { pattern: /\b(?:fetch|XMLHttpRequest)\s*\(/, label: 'network request' },
  { pattern: /\bnew\s+XMLHttpRequest\s*\(/, label: 'network request' },
  { pattern: /\.(?:appendChild|removeChild|replaceChild|insertBefore|insertAdjacentHTML|insertAdjacentElement|setAttribute|removeAttribute|toggleAttribute|click|focus|blur|submit|requestSubmit|dispatchEvent)\s*\(/, label: 'DOM or interaction mutation' },
  { pattern: /\.classList\.(?:add|remove|toggle|replace)\s*\(/, label: 'classList mutation' },
  { pattern: /\.(?:innerHTML|outerHTML|textContent|innerText|value|checked|disabled|selected|selectedIndex|style)\s*=/, label: 'DOM property mutation' },
  { pattern: /\.style\.[A-Za-z_$][\w$]*\s*=/, label: 'style mutation' },
  { pattern: /\b(?:eval|Function)\s*\(/, label: 'dynamic code execution' },
  { pattern: /\bnew\s+Function\s*\(/, label: 'dynamic code execution' }
]

function asObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : {}
}

function asArray(value) {
  return Array.isArray(value) ? value : []
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, Math.max(0, Number(ms) || 0)))
}

function maskJavaScriptLiteralsAndComments(source) {
  let output = ''
  let index = 0
  let state = 'code'
  while (index < source.length) {
    const char = source[index] ?? ''
    const next = source[index + 1] ?? ''
    if (state === 'code') {
      if (char === "'" || char === '"' || char === '`') {
        state = char === "'" ? 'single' : char === '"' ? 'double' : 'template'
        output += ' '
        index += 1
        continue
      }
      if (char === '/' && next === '/') {
        state = 'lineComment'
        output += '  '
        index += 2
        continue
      }
      if (char === '/' && next === '*') {
        state = 'blockComment'
        output += '  '
        index += 2
        continue
      }
      output += char
      index += 1
      continue
    }
    if (state === 'lineComment') {
      output += char === '\n' ? '\n' : ' '
      if (char === '\n') state = 'code'
      index += 1
      continue
    }
    if (state === 'blockComment') {
      output += char === '\n' ? '\n' : ' '
      if (char === '*' && next === '/') {
        output += ' '
        index += 2
        state = 'code'
      } else {
        index += 1
      }
      continue
    }
    output += char === '\n' ? '\n' : ' '
    if (char === '\\') {
      output += next === '\n' ? '\n' : ' '
      index += 2
      continue
    }
    if ((state === 'single' && char === "'") || (state === 'double' && char === '"') || (state === 'template' && char === '`')) {
      state = 'code'
    }
    index += 1
  }
  return output
}

function readonlyEvaluateViolation(label) {
  return new Error(`${READONLY_EVALUATE_ERROR}: ${label} is not allowed in tab.playwright.evaluate. Use locators, CUA, DOM-CUA, navigation helpers, or wait helpers for page side effects.`)
}

function assertReadOnlyEvaluateSource(source) {
  const stripped = maskJavaScriptLiteralsAndComments(source)
  for (const { pattern, label } of READONLY_EVALUATE_DENY_PATTERNS) {
    if (pattern.test(stripped)) throw readonlyEvaluateViolation(label)
  }
}

function readOnlyEvaluateExpression(source) {
  assertReadOnlyEvaluateSource(source)
  return `(() => {
    const __dotcraftSource = ${JSON.stringify(source)};
    const __dotcraftRestore = [];
    const __dotcraftThrow = (name) => {
      throw new Error(${JSON.stringify(READONLY_EVALUATE_ERROR)} + ': ' + name + ' is not allowed in tab.playwright.evaluate. Use locators, CUA, DOM-CUA, navigation helpers, or wait helpers for page side effects.');
    };
    const __dotcraftPatch = (target, key, label) => {
      try {
        if (!target) return;
        const original = target[key];
        if (typeof original !== 'function') return;
        target[key] = function readonlyEvaluateBlocked() { __dotcraftThrow(label); };
        __dotcraftRestore.push(() => {
          try { target[key] = original; } catch (_) {}
        });
      } catch (_) {}
    };
    __dotcraftPatch(globalThis, 'fetch', 'fetch');
    __dotcraftPatch(globalThis, 'XMLHttpRequest', 'XMLHttpRequest');
    __dotcraftPatch(globalThis, 'open', 'window.open');
    __dotcraftPatch(globalThis, 'scrollTo', 'window.scrollTo');
    __dotcraftPatch(globalThis, 'scrollBy', 'window.scrollBy');
    __dotcraftPatch(globalThis.navigator, 'sendBeacon', 'navigator.sendBeacon');
    __dotcraftPatch(globalThis.history, 'pushState', 'history.pushState');
    __dotcraftPatch(globalThis.history, 'replaceState', 'history.replaceState');
    __dotcraftPatch(globalThis.history, 'go', 'history.go');
    __dotcraftPatch(globalThis.history, 'back', 'history.back');
    __dotcraftPatch(globalThis.history, 'forward', 'history.forward');
    __dotcraftPatch(globalThis.location, 'assign', 'location.assign');
    __dotcraftPatch(globalThis.location, 'replace', 'location.replace');
    __dotcraftPatch(globalThis.location, 'reload', 'location.reload');
    __dotcraftPatch(globalThis.localStorage, 'setItem', 'localStorage.setItem');
    __dotcraftPatch(globalThis.localStorage, 'removeItem', 'localStorage.removeItem');
    __dotcraftPatch(globalThis.localStorage, 'clear', 'localStorage.clear');
    __dotcraftPatch(globalThis.sessionStorage, 'setItem', 'sessionStorage.setItem');
    __dotcraftPatch(globalThis.sessionStorage, 'removeItem', 'sessionStorage.removeItem');
    __dotcraftPatch(globalThis.sessionStorage, 'clear', 'sessionStorage.clear');
    __dotcraftPatch(globalThis.Document?.prototype, 'write', 'document.write');
    __dotcraftPatch(globalThis.Document?.prototype, 'writeln', 'document.writeln');
    __dotcraftPatch(globalThis.Node?.prototype, 'appendChild', 'Node.appendChild');
    __dotcraftPatch(globalThis.Node?.prototype, 'removeChild', 'Node.removeChild');
    __dotcraftPatch(globalThis.Node?.prototype, 'replaceChild', 'Node.replaceChild');
    __dotcraftPatch(globalThis.Node?.prototype, 'insertBefore', 'Node.insertBefore');
    __dotcraftPatch(globalThis.Element?.prototype, 'setAttribute', 'Element.setAttribute');
    __dotcraftPatch(globalThis.Element?.prototype, 'removeAttribute', 'Element.removeAttribute');
    __dotcraftPatch(globalThis.Element?.prototype, 'toggleAttribute', 'Element.toggleAttribute');
    __dotcraftPatch(globalThis.Element?.prototype, 'insertAdjacentHTML', 'Element.insertAdjacentHTML');
    __dotcraftPatch(globalThis.Element?.prototype, 'insertAdjacentElement', 'Element.insertAdjacentElement');
    __dotcraftPatch(globalThis.EventTarget?.prototype, 'dispatchEvent', 'EventTarget.dispatchEvent');
    __dotcraftPatch(globalThis.HTMLElement?.prototype, 'click', 'HTMLElement.click');
    __dotcraftPatch(globalThis.HTMLElement?.prototype, 'focus', 'HTMLElement.focus');
    __dotcraftPatch(globalThis.HTMLElement?.prototype, 'blur', 'HTMLElement.blur');
    __dotcraftPatch(globalThis.HTMLFormElement?.prototype, 'submit', 'HTMLFormElement.submit');
    __dotcraftPatch(globalThis.HTMLFormElement?.prototype, 'requestSubmit', 'HTMLFormElement.requestSubmit');
    return (async () => {
      try {
        return await (0, eval)(__dotcraftSource);
      } finally {
        for (let i = __dotcraftRestore.length - 1; i >= 0; i -= 1) __dotcraftRestore[i]();
      }
    })();
  })()`
}

function frameMessage(payload) {
  const body = Buffer.from(JSON.stringify(payload), 'utf8')
  const frame = Buffer.allocUnsafe(FRAME_HEADER_BYTES + body.byteLength)
  frame.writeUInt32LE(body.byteLength, 0)
  body.copy(frame, FRAME_HEADER_BYTES)
  return frame
}

class FrameDecoder {
  buffer = Buffer.alloc(0)

  push(chunk) {
    if (chunk?.byteLength > 0) {
      this.buffer = Buffer.concat([
        this.buffer,
        Buffer.from(chunk.buffer, chunk.byteOffset, chunk.byteLength)
      ])
    }
    const messages = []
    for (;;) {
      if (this.buffer.byteLength < FRAME_HEADER_BYTES) break
      const length = this.buffer.readUInt32LE(0)
      if (length > 16 * 1024 * 1024) throw new Error('Browser backend frame exceeds the maximum allowed size.')
      const frameLength = FRAME_HEADER_BYTES + length
      if (this.buffer.byteLength < frameLength) break
      messages.push(JSON.parse(this.buffer.subarray(FRAME_HEADER_BYTES, frameLength).toString('utf8')))
      this.buffer = this.buffer.subarray(frameLength)
    }
    return messages
  }
}

class BrowserClientError extends Error {
  constructor(error) {
    const details = asObject(error)
    const message = typeof details.message === 'string' && details.message
      ? details.message
      : String(error)
    super(message)
    this.name = 'BrowserClientError'
    if (details.code != null) this.code = details.code
    if (details.data != null) this.data = details.data
    const category = /^([A-Za-z][A-Za-z0-9_]*):/.exec(message)?.[1]
    if (category) this.category = category
  }
}

class RpcTransport {
  constructor(socket) {
    this.socket = socket
    this.decoder = new FrameDecoder()
    this.nextId = 1
    this.pending = new Map()
    this.eventHandlers = new Map()
    socket.on?.('data', (chunk) => this.handleData(chunk))
    socket.on?.('error', (error) => this.closePending(error))
    socket.on?.('close', () => this.closePending(new Error('native pipe closed before response')))
  }

  request(method, params = {}) {
    const id = this.nextId++
    const payload = { jsonrpc: '2.0', id, method, params }
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject })
      try {
        this.socket.write(frameMessage(payload))
      } catch (error) {
        this.pending.delete(id)
        reject(error)
      }
    })
  }

  on(method, handler) {
    const current = this.eventHandlers.get(method) ?? []
    current.push(handler)
    this.eventHandlers.set(method, current)
    return () => this.eventHandlers.set(method, (this.eventHandlers.get(method) ?? []).filter((item) => item !== handler))
  }

  async close() {
    await new Promise((resolve) => {
      try {
        this.socket.end(resolve)
      } catch {
        resolve()
      }
    })
  }

  handleData(chunk) {
    let messages
    try {
      messages = this.decoder.push(chunk)
    } catch (error) {
      this.closePending(error)
      return
    }
    for (const message of messages) this.handleMessage(message)
  }

  handleMessage(message) {
    if (message?.id != null) {
      const pending = this.pending.get(message.id)
      if (!pending) return
      this.pending.delete(message.id)
      if (message.error) {
        pending.reject(new BrowserClientError(message.error))
      } else {
        pending.resolve(message.result)
      }
      return
    }
    if (typeof message?.method === 'string') {
      for (const handler of this.eventHandlers.get(message.method) ?? []) {
        Promise.resolve().then(() => handler(message.params)).catch(() => {})
      }
    }
  }

  closePending(error) {
    for (const pending of this.pending.values()) pending.reject(error)
    this.pending.clear()
  }
}

class BackendApi {
  constructor(transport, getTurnMetadata) {
    this.transport = transport
    this.getTurnMetadata = getTurnMetadata
  }

  addEventListener(method, handler) {
    return this.transport.on(method, handler)
  }

  async close() {
    await this.transport.close()
  }

  request(method, params = {}) {
    return this.transport.request(method, method === 'ping' ? params : { ...params, ...this.sessionParams() })
  }

  ping() { return this.request('ping') }
  getInfo() { return this.request('getInfo') }
  getTabs() { return this.request('getTabs') }
  getUserTabs() { return this.request('getUserTabs') }
  getUserHistory(params) { return this.request('getUserHistory', params) }
  claimUserTab(tabId) { return this.request('claimUserTab', { tabId: Number(tabId) }) }
  createTab(params = {}) { return this.request('createTab', params) }
  finalizeTabs(keep) { return this.request('finalizeTabs', { keep }) }
  nameSession(name) { return this.request('nameSession', { name }) }
  attach(tabId) { return this.request('attach', { tabId: Number(tabId) }) }
  detach(tabId) { return this.request('detach', { tabId: Number(tabId) }) }
  attachTarget(tabId, targetId) { return this.request('attachTarget', { tabId: Number(tabId), targetId }) }
  detachTarget(tabId, targetId) { return this.request('detachTarget', { tabId: Number(tabId), targetId }) }
  moveMouse(params) { return this.request('moveMouse', params) }
  executeUnhandledCommand(params) { return this.request('executeUnhandledCommand', params) }
  executeCdp(target, method, commandParams = {}, timeoutMs) {
    return this.request('executeCdp', { target, method, commandParams, timeoutMs })
  }

  sessionParams() {
    const metadata = asObject(this.getTurnMetadata())
    const sessionId = metadata.session_id ?? metadata.sessionId
    const turnId = metadata.turn_id ?? metadata.turnId ?? metadata.evaluation_id ?? metadata.evaluationId
    if (typeof sessionId !== 'string' || !sessionId) throw new Error('Missing required browser session_id')
    if (typeof turnId !== 'string' || !turnId) throw new Error('Missing required browser turn_id')
    return {
      session_id: sessionId,
      turn_id: turnId,
      evaluation_id: metadata.evaluation_id ?? metadata.evaluationId
    }
  }
}

function turnMetadata(globals) {
  const fromRequest = globals?.nodeRepl?.requestMeta?.[REQUEST_META_KEY]
  if (fromRequest && typeof fromRequest === 'object') return fromRequest
  const session = globals?.dotcraft?.browserSession
  if (!session || typeof session !== 'object') return null
  return {
    session_id: session.sessionId,
    thread_id: session.threadId,
    turn_id: session.turnId ?? session.evaluationId,
    evaluation_id: session.evaluationId,
    backend_id: session.backendId ?? 'iab'
  }
}

async function candidatePipePaths(globals) {
  if (platform() === 'win32') {
    const root = '\\\\.\\pipe\\'
    try {
      return (await readdir(root))
        .map((name) => `${root}${name}`)
        .filter((candidate) => candidate.toLowerCase().startsWith(WINDOWS_PIPE_PREFIX))
    } catch {
      return []
    }
  }
  const dir = path.join(globals?.nodeRepl?.tmpDir ?? tmpdir(), PIPE_DIR_NAME)
  try {
    return (await readdir(dir)).map((name) => path.join(dir, name)).filter((candidate) => /[/\\]dotcraft-[^/\\]+\.sock$/.test(candidate))
  } catch {
    return []
  }
}

async function connectCandidate(globals, candidate, getTurnMetadata) {
  const socket = await globals.nodeRepl.nativePipe.createConnection(candidate)
  const transport = new RpcTransport(socket)
  const api = new BackendApi(transport, getTurnMetadata)
  try {
    const info = await api.getInfo()
    return { api, info }
  } catch (error) {
    await api.close().catch(() => {})
    throw error
  }
}

async function discoverIabBackends(globals, getTurnMetadata) {
  const sessionId = getTurnMetadata()?.session_id
  for (let attempt = 0; attempt < 5; attempt += 1) {
    const paths = await candidatePipePaths(globals)
    const discovered = []
    for (const candidate of paths) {
      try {
        const backend = await connectCandidate(globals, candidate, getTurnMetadata)
        const info = asObject(backend.info)
        const metadata = asObject(info.metadata)
        if ((info.type ?? info.id) !== 'iab') {
          await backend.api.close().catch(() => {})
          continue
        }
        if (sessionId && metadata.dotcraftSessionId && metadata.dotcraftSessionId !== sessionId) {
          await backend.api.close().catch(() => {})
          continue
        }
        discovered.push(backend)
      } catch {
        // Discovery is best-effort across stale or foreign pipe candidates.
      }
    }
    if (discovered.length > 0) return discovered
    await sleep(25)
  }
  return []
}

function normalizeInfo(info) {
  const raw = asObject(info)
  return {
    ...raw,
    id: 'iab',
    type: 'iab',
    name: String(raw.name ?? 'DotCraft Browser'),
    metadata: asObject(raw.metadata),
    capabilities: {
      browser: asArray(asObject(raw.capabilities).browser),
      tab: asArray(asObject(raw.capabilities).tab),
      docs: asObject(asObject(raw.capabilities).docs)
    }
  }
}

function tabIdOf(tabLike) {
  if (tabLike instanceof TabHandle) return tabLike.numericId
  if (typeof tabLike === 'number') return Math.trunc(tabLike)
  if (typeof tabLike === 'string') return Number(tabLike)
  if (tabLike && typeof tabLike === 'object') return tabIdOf(tabLike.id ?? tabLike.tabId ?? tabLike.tab_id)
  return NaN
}

function normalizeTabInfo(raw) {
  const info = asObject(raw)
  const id = tabIdOf(info.id ?? info.tabId ?? info.tab_id)
  return {
    id: Number.isFinite(id) && id > 0 ? id : 0,
    tabId: Number.isFinite(id) && id > 0 ? id : 0,
    url: typeof info.url === 'string' ? info.url : 'about:blank',
    title: typeof info.title === 'string' ? info.title : '',
    loading: info.loading === true,
    active: info.active === true
  }
}

function plainTabInfo(raw) {
  const info = normalizeTabInfo(raw)
  return {
    id: String(info.id),
    tabId: String(info.tabId),
    title: info.title,
    url: info.url,
    loading: info.loading,
    active: info.active
  }
}

function imageResult(dataBase64, mediaType = 'image/png') {
  const length = Buffer.from(String(dataBase64 ?? ''), 'base64').byteLength
  return { mediaType, dataBase64: String(dataBase64 ?? ''), length }
}

function cdpValue(result) {
  const response = asObject(result)
  if (response.exceptionDetails) {
    const details = asObject(response.exceptionDetails)
    const exception = asObject(details.exception)
    throw new Error(String(exception.description ?? exception.value ?? details.text ?? 'Runtime.evaluate failed'))
  }
  const runtimeResult = asObject(response.result)
  return runtimeResult.value ?? runtimeResult.unserializableValue
}

function resultSizeGuard(value) {
  const serialized = typeof value === 'string' ? value : JSON.stringify(value)
  if (serialized && Buffer.byteLength(serialized, 'utf8') > MAX_RESULT_BYTES) {
    throw new Error(`ResultTooLarge: browser result exceeded ${MAX_RESULT_BYTES} bytes.`)
  }
  return value
}

function jsString(value) {
  return JSON.stringify(String(value ?? ''))
}

function jsLiteral(value) {
  return JSON.stringify(value)
}

function selectorFor(descriptor) {
  let base
  if (descriptor.kind === 'chain') base = `${selectorFor(asObject(descriptor.parent))} >> ${selectorFor(asObject(descriptor.child))}`
  else if (descriptor.kind === 'and') base = `(${selectorFor(asObject(descriptor.left))}).and(${selectorFor(asObject(descriptor.right))})`
  else if (descriptor.kind === 'or') base = `(${selectorFor(asObject(descriptor.left))}).or(${selectorFor(asObject(descriptor.right))})`
  else if (descriptor.kind === 'role') base = `role=${descriptor.value}${descriptor.name ? `[name=${JSON.stringify(descriptor.name)}]` : ''}`
  else if (descriptor.kind === 'text') base = `text=${descriptor.value}`
  else if (descriptor.kind === 'label') base = `label=${descriptor.value}`
  else if (descriptor.kind === 'placeholder') base = `[placeholder=${JSON.stringify(descriptor.value)}]`
  else if (descriptor.kind === 'testId') base = `[data-testid=${JSON.stringify(descriptor.value)}]`
  else base = String(descriptor.value ?? '*')
  const filters = asArray(descriptor.filters)
  return filters.length ? `${base}.filter(${filters.map((filter) => String(asObject(filter).kind ?? '')).join(',')})` : base
}

function locatorDescriptor(descriptor) {
  const raw = asObject(descriptor)
  const normalized = {
    kind: String(raw.kind ?? 'css'),
    value: raw.value == null ? undefined : String(raw.value),
    name: raw.name == null ? undefined : String(raw.name),
    exact: raw.exact === true,
    index: Number.isInteger(raw.index) ? raw.index : undefined,
    frameSelectors: asArray(raw.frameSelectors).map(String)
  }
  if (raw.parent) normalized.parent = locatorDescriptor(raw.parent)
  if (raw.child) normalized.child = locatorDescriptor(raw.child)
  if (raw.left) normalized.left = locatorDescriptor(raw.left)
  if (raw.right) normalized.right = locatorDescriptor(raw.right)
  const filters = asArray(raw.filters).map((filter) => {
    const item = asObject(filter)
    const normalizedFilter = {
      kind: String(item.kind ?? ''),
      value: item.value,
      matcher: item.matcher && typeof item.matcher === 'object' ? asObject(item.matcher) : undefined
    }
    if (item.descriptor) normalizedFilter.descriptor = locatorDescriptor(item.descriptor)
    return normalizedFilter
  }).filter((filter) => filter.kind)
  if (filters.length > 0) normalized.filters = filters
  return normalized
}

function textMatcherDescriptor(value, options = {}) {
  if (value instanceof RegExp) {
    return { pattern: value.source, flags: value.flags }
  }
  return { value: String(value ?? ''), exact: asObject(options).exact === true }
}

function descriptorFromLocator(value, optionName) {
  if (value instanceof Locator) return value.descriptor
  throw new Error(`InvalidArgument: locator ${optionName} option requires another Locator.`)
}

function locatorFiltersFromOptions(options = {}) {
  const raw = asObject(options)
  const filters = []
  if (Object.prototype.hasOwnProperty.call(raw, 'hasText') && raw.hasText != null) {
    filters.push({ kind: 'hasText', matcher: textMatcherDescriptor(raw.hasText, raw) })
  }
  if (Object.prototype.hasOwnProperty.call(raw, 'hasNotText') && raw.hasNotText != null) {
    filters.push({ kind: 'hasNotText', matcher: textMatcherDescriptor(raw.hasNotText, raw) })
  }
  if (Object.prototype.hasOwnProperty.call(raw, 'visible') && typeof raw.visible === 'boolean') {
    filters.push({ kind: 'visible', value: raw.visible })
  }
  if (raw.has != null) {
    filters.push({ kind: 'has', descriptor: descriptorFromLocator(raw.has, 'has') })
  }
  if (raw.hasNot != null) {
    filters.push({ kind: 'hasNot', descriptor: descriptorFromLocator(raw.hasNot, 'hasNot') })
  }
  return filters
}

function withLocatorOptions(descriptor, options = {}) {
  const base = locatorDescriptor(descriptor)
  const filters = locatorFiltersFromOptions(options)
  if (filters.length === 0) return base
  return { ...base, filters: [...asArray(base.filters), ...filters] }
}

function unsupportedDownload(browserName) {
  return `Downloads are not supported by ${browserName}.`
}

function unsupportedUpload(browserName) {
  return `File uploads are not supported by ${browserName}.`
}

class BrowserHandle {
  constructor(api, info, globals) {
    this.api = api
    this.info = normalizeInfo(info)
    this.globals = globals
    this.browserId = 'iab'
    this.id = 'iab'
    this.name = this.info.name
    this.tabs = this.createTabsApi()
    this.user = this.createUserApi()
    this.capabilities = this.createCapabilitiesApi()
  }

  async documentation() { return browserDocumentation(this.info) }

  async nameSession(name) {
    return await this.api.nameSession(String(name ?? ''))
  }

  async goto(url) {
    const tab = await this.tabs.selected() ?? await this.tabs.new()
    await tab.goto(url)
    return tab
  }

  createTabsApi() {
    return {
      list: async () => (await this.api.getTabs()).map(plainTabInfo),
      new: async (url) => {
        const tab = new TabHandle(this, normalizeTabInfo(await this.api.createTab(url ? { url: String(url) } : {})))
        if (url) await tab.refresh()
        return tab
      },
      selected: async () => {
        const tabs = (await this.api.getTabs()).map(normalizeTabInfo)
        const selected = tabs.find((tab) => tab.active)
        return selected ? new TabHandle(this, selected) : undefined
      },
      get: async (id) => {
        const numeric = tabIdOf(id)
        const found = (await this.api.getTabs()).map(normalizeTabInfo).find((tab) => tab.id === numeric)
        if (!found) throw new Error(`TabStale: Browser tab is no longer available: ${String(id)}`)
        return new TabHandle(this, found)
      },
      content: async (options = {}) => {
        const result = await this.api.executeUnhandledCommand({
          type: 'tabs_content',
          urls: asArray(options.urls).map(String),
          content_type: options.content_type ?? options.contentType ?? 'text',
          timeoutMs: options.timeoutMs
        })
        return asArray(asObject(result).results)
      },
      finalize: async (options = {}) => {
        const keep = asArray(options.keep).map((entry) => {
          const item = asObject(entry)
          return { tabId: tabIdOf(item.tab ?? item.tabId ?? item.id), status: item.status }
        })
        return await this.api.finalizeTabs(keep)
      },
      describeApi: () => ['list() -> TabInfo[]', 'new(url?) -> Tab', 'selected() -> Tab|undefined', 'get(id) -> Tab', 'content({ urls, contentType })', 'finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })']
    }
  }

  createUserApi() {
    return {
      openTabs: async () => (await this.api.getUserTabs()).map(plainTabInfo),
      claimTab: async (tab) => {
        const claimed = await this.api.claimUserTab(tabIdOf(tab))
        return claimed ? new TabHandle(this, normalizeTabInfo(claimed)) : null
      },
      history: async (options) => await this.api.getUserHistory(options ?? {}),
      describeApi: () => ['openTabs() -> TabInfo[]', 'claimTab(tabOrId) -> Tab|null', 'history(options) unsupported']
    }
  }

  createCapabilitiesApi() {
    return {
      list: async () => this.info.capabilities.browser,
      get: (id) => {
        if (id === 'visibility') {
          return {
            get: async () => asObject(await this.api.executeUnhandledCommand({ type: 'browser_visibility_get' })).visible === true,
            set: async (visible) => await this.api.executeUnhandledCommand({ type: 'browser_visibility_set', visible: visible === true }),
            describeApi: () => ['get()', 'set(visible)']
          }
        }
        if (id === 'viewport') {
          return {
            set: async (options = {}) => await this.api.executeUnhandledCommand({
              type: 'browser_viewport_set',
              width: Number(options.width),
              height: Number(options.height)
            }),
            reset: async () => await this.api.executeUnhandledCommand({ type: 'browser_viewport_reset' }),
            describeApi: () => ['set({ width, height })', 'reset()']
          }
        }
        throw new Error(`Browser capability not found: ${id}. Available capabilities: visibility, viewport.`)
      },
      describeApi: () => ['list()', 'get("visibility")', 'get("viewport")']
    }
  }

  describeApi() {
    return ['documentation()', 'nameSession(name)', 'goto(url)', 'tabs.*', 'user.*', 'capabilities.*']
  }
}

class TabHandle {
  constructor(browser, info) {
    this.browser = browser
    this.api = browser.api
    this.info = normalizeTabInfo(info)
    this.numericId = this.info.id
    this.id = String(this.info.id)
    this.tabId = String(this.info.id)
    this.playwright = new PlaywrightApi(this)
    this.cua = new CuaApi(this)
    this.dom_cua = new DomCuaApi(this)
    this.capabilities = this.createCapabilitiesApi()
    this.dev = {
      logs: async (options = {}) => asArray(asObject(await this.api.executeUnhandledCommand({
        type: 'tab_dev_logs',
        tab_id: this.numericId,
        ...options
      })).logs),
      describeApi: () => ['logs({ filter?, levels?, limit? })']
    }
    this.clipboard = new ClipboardApi(this)
    this.content = {
      export: async () => await this.api.executeUnhandledCommand({ type: 'tab_content_export', tab_id: this.numericId }),
      exportGsuite: async () => { throw new Error(unsupportedDownload(this.browser.name)) },
      describeApi: () => ['export() unsupported', 'exportGsuite(format) unsupported']
    }
  }

  async markDeliverable() { await this.api.executeUnhandledCommand({ type: 'tab_mark', tab_id: this.numericId, status: 'deliverable' }) }
  async markHandoff() { await this.api.executeUnhandledCommand({ type: 'tab_mark', tab_id: this.numericId, status: 'handoff' }) }

  target() {
    return { tabId: this.numericId }
  }

  async cdp(method, commandParams = {}, timeoutMs) {
    return await this.api.executeCdp(this.target(), method, commandParams, timeoutMs)
  }

  async eval(expression, timeoutMs) {
    return resultSizeGuard(cdpValue(await this.cdp('Runtime.evaluate', {
      expression,
      awaitPromise: true,
      returnByValue: true,
      userGesture: true
    }, timeoutMs)))
  }

  async refresh() {
    const found = (await this.api.getTabs()).map(normalizeTabInfo).find((tab) => tab.id === this.numericId)
    if (found) this.info = found
    return this.info
  }

  async goto(url) {
    await this.cdp('Page.navigate', { url: String(url) })
    await this.refresh()
    return this
  }

  navigate(url) { return this.goto(url) }
  async back() { await this.api.executeUnhandledCommand({ type: 'navigate_tab_back', tab_id: this.numericId }); await this.refresh(); return this }
  async forward() { await this.api.executeUnhandledCommand({ type: 'navigate_tab_forward', tab_id: this.numericId }); await this.refresh(); return this }
  async reload() { await this.api.executeUnhandledCommand({ type: 'navigate_tab_reload', tab_id: this.numericId }); await this.refresh(); return this }
  async close() { await this.api.executeUnhandledCommand({ type: 'close_tab', tab_id: this.numericId }) }
  async url() { return (await this.refresh()).url }
  async title() { return (await this.refresh()).title }

  async screenshot(options = {}) {
    const clip = asObject(options.clip)
    const result = asObject(await this.api.executeUnhandledCommand({
      type: 'tab_screenshot',
      tab_id: this.numericId,
      fullPage: options.fullPage === true,
      ...(clip.width != null || clip.height != null
        ? {
            cropX: Number(clip.x ?? 0),
            cropY: Number(clip.y ?? 0),
            cropWidth: Number(clip.width),
            cropHeight: Number(clip.height)
          }
        : {})
    }))
    return imageResult(result.data ?? '')
  }

  async domSnapshot() {
    return await this.playwright.domSnapshot()
  }

  async evaluate(expressionOrFunction, arg, options) {
    return await this.playwright.evaluate(expressionOrFunction, arg, options)
  }

  async waitForLoadState(stateOrOptions, timeoutMs) {
    return await this.playwright.waitForLoadState(stateOrOptions, timeoutMs)
  }

  async waitForURL(url, options) {
    return await this.playwright.waitForURL(url, options)
  }

  locator(selector, options) { return this.playwright.locator(selector, options) }
  getByRole(role, options) { return this.playwright.getByRole(role, options) }
  getByText(text, options) { return this.playwright.getByText(text, options) }
  getByLabel(text, options) { return this.playwright.getByLabel(text, options) }
  getByPlaceholder(text, options) { return this.playwright.getByPlaceholder(text, options) }
  getByTestId(testId) { return this.playwright.getByTestId(testId) }

  async isWebMcpAvailable() {
    try {
      const available = await this.eval(`(() => {
        const __dotcraftWebMcpAvailabilityProbe = true;
        const modelContext = typeof navigator !== "undefined" ? navigator.modelContext : undefined;
        return !!modelContext &&
          typeof modelContext.getTools === "function" &&
          typeof modelContext.executeTool === "function";
      })()`)
      return available === true
    } catch {
      return false
    }
  }

  createCapabilitiesApi() {
    return {
      list: async () => {
        const available = asArray(this.browser.info.capabilities.tab)
          .filter((capability) => asObject(capability).id !== 'webmcp')
        if (await this.isWebMcpAvailable()) available.push({ ...WEBMCP_CAPABILITY })
        return available
      },
      get: async (id) => {
        if (id === 'pageAssets') return new PageAssetsCapability(this)
        if (id === 'webmcp') {
          if (await this.isWebMcpAvailable()) return new WebMcpCapability(this)
          throw new Error(WEBMCP_UNAVAILABLE_ERROR)
        }
        const available = (await this.capabilities.list()).map((capability) => asObject(capability).id).join(', ')
        throw new Error(`Tab capability not found: ${id}. Available capabilities: ${available}.`)
      },
      describeApi: () => ['list()', 'get("pageAssets")', 'get("webmcp")']
    }
  }

  describeApi() {
    return ['goto(url)', 'reload()', 'back()', 'forward()', 'close()', 'markDeliverable()', 'markHandoff()', 'url()', 'title()', 'screenshot(options?)', 'playwright.*', 'cua.*', 'dom_cua.*', 'capabilities.*']
  }

  toJSON() {
    return plainTabInfo(this.info)
  }
}

class PlaywrightApi {
  constructor(tab) {
    this.tab = tab
  }

  async evaluate(expressionOrFunction, arg, options = {}) {
    const expression = typeof expressionOrFunction === 'function'
      ? `((fn, arg) => fn(arg))(${expressionOrFunction.toString()}, ${jsLiteral(arg)})`
      : String(expressionOrFunction)
    return await this.tab.eval(readOnlyEvaluateExpression(expression), options?.timeoutMs ?? options?.timeout)
  }

  async domSnapshot() {
    const result = await this.tab.api.executeUnhandledCommand({ type: 'playwright_dom_snapshot', tab_id: this.tab.numericId })
    return String(asObject(result).dom_snapshot ?? '')
  }

  async screenshot(options) { return await this.tab.screenshot(options) }
  async waitForTimeout(timeoutMs) { await sleep(timeoutMs) }
  async waitForURL(url, options = {}) {
    const expected = String(url)
    const deadline = Date.now() + Math.max(1, Number(options.timeoutMs ?? options.timeout ?? 30000))
    for (;;) {
      if (await this.tab.url() === expected) return
      if (Date.now() > deadline) throw new Error(`CommandTimeout: waitForURL timed out waiting for ${expected}`)
      await sleep(50)
    }
  }
  async waitForLoadState(stateOrOptions = {}, timeoutMs) {
    const options = typeof stateOrOptions === 'object' && stateOrOptions !== null ? stateOrOptions : {}
    const state = typeof stateOrOptions === 'string' ? stateOrOptions : options.state
    await this.tab.api.executeUnhandledCommand({
      type: 'playwright_wait_for_load_state',
      tab_id: this.tab.numericId,
      state: state ?? 'load',
      timeout_ms: options.timeoutMs ?? options.timeout ?? timeoutMs
    })
  }
  async waitForEvent(event) {
    if (event === 'download') throw new Error(unsupportedDownload(this.tab.browser.name))
    if (event === 'filechooser') throw new Error(unsupportedUpload(this.tab.browser.name))
    throw new Error(`UnsupportedApi: tab.playwright.waitForEvent("${event}")`)
  }
  async expectNavigation(action, options = {}) {
    const result = await action()
    if (options.url) await this.waitForURL(options.url, options)
    else await this.waitForLoadState(options)
    return result
  }

  locator(selector, options = {}) { return new Locator(this.tab, withLocatorOptions({ kind: 'css', value: String(selector) }, options)) }
  getByRole(role, options = {}) { return new Locator(this.tab, { kind: 'role', value: String(role), name: options.name, exact: options.exact === true }) }
  getByText(text, options = {}) { return new Locator(this.tab, { kind: 'text', value: String(text), exact: options.exact === true }) }
  getByLabel(text, options = {}) { return new Locator(this.tab, { kind: 'label', value: String(text), exact: options.exact === true }) }
  getByPlaceholder(text, options = {}) { return new Locator(this.tab, { kind: 'placeholder', value: String(text), exact: options.exact === true }) }
  getByTestId(testId) { return new Locator(this.tab, { kind: 'testId', value: String(testId) }) }
  frameLocator(selector) { return new FrameLocator(this.tab, String(selector)) }
  describeApi() { return ['evaluate(fnOrExpression, arg?, options?) read-only', 'domSnapshot()', 'screenshot(options?)', 'waitForURL(url, options?)', 'waitForLoadState(options?)', 'waitForTimeout(ms)', 'expectNavigation(action, options?)', 'locator(selector, options?)', 'getByRole(role, options?)', 'getByText(text, options?)', 'getByLabel(text, options?)', 'getByPlaceholder(text, options?)', 'getByTestId(testId)', 'frameLocator(selector)'] }
}

class FrameLocator {
  constructor(tab, frameSelectors) {
    this.tab = tab
    this.frameSelectors = asArray(Array.isArray(frameSelectors) ? frameSelectors : [frameSelectors]).map(String)
  }
  inFrame(descriptor) { return { ...descriptor, frameSelectors: this.frameSelectors } }
  locator(selector, options = {}) { return new Locator(this.tab, this.inFrame(withLocatorOptions({ kind: 'css', value: String(selector) }, options))) }
  getByRole(role, options = {}) { return new Locator(this.tab, this.inFrame({ kind: 'role', value: String(role), name: options.name, exact: options.exact === true })) }
  getByText(text, options = {}) { return new Locator(this.tab, this.inFrame({ kind: 'text', value: String(text), exact: options.exact === true })) }
  getByLabel(text, options = {}) { return new Locator(this.tab, this.inFrame({ kind: 'label', value: String(text), exact: options.exact === true })) }
  getByPlaceholder(text, options = {}) { return new Locator(this.tab, this.inFrame({ kind: 'placeholder', value: String(text), exact: options.exact === true })) }
  getByTestId(testId) { return new Locator(this.tab, this.inFrame({ kind: 'testId', value: String(testId) })) }
  frameLocator(selector) { return new FrameLocator(this.tab, [...this.frameSelectors, String(selector)]) }
  describeApi() { return ['locator(selector, options?)', 'getByRole(role, options?)', 'getByText(text, options?)', 'getByLabel(text, options?)', 'getByPlaceholder(text, options?)', 'getByTestId(testId)', 'frameLocator(selector)'] }
}

class Locator {
  constructor(tab, descriptor) {
    this.tab = tab
    this.descriptor = locatorDescriptor(descriptor)
    this.selector = selectorFor(descriptor)
  }

  withDescriptor(next) { return new Locator(this.tab, next) }
  scoped(child) {
    return this.withDescriptor({
      kind: 'chain',
      parent: this.descriptor,
      child: locatorDescriptor(child),
      frameSelectors: this.descriptor.frameSelectors
    })
  }
  assertSameFrameLocator(other) {
    if (!(other instanceof Locator)) throw new Error('InvalidArgument: locator.and/or requires another Locator.')
    if (other.tab !== this.tab) throw new Error('InvalidArgument: locator.and/or requires locators from the same tab.')
    if (JSON.stringify(other.descriptor.frameSelectors || []) !== JSON.stringify(this.descriptor.frameSelectors || [])) {
      throw new Error('UnsupportedApi: locator.and/or across different frame scopes is not supported by DotCraft Desktop IAB.')
    }
  }

  async matches() {
    const result = await this.locatorOperation('resolve')
    return asArray(result).map((item) => asObject(item))
  }

  async strictMatch() {
    const matches = await this.matches()
    if (matches.length === 0) throw new Error(`No element found for locator: ${this.selector}`)
    if (matches.length > 1) throw new Error(`Strict mode violation for locator ${this.selector}: ${matches.length} elements matched.`)
    return matches[0]
  }

  async locatorOperation(operation, payload = {}) {
    const result = await this.tab.api.executeUnhandledCommand({
      type: 'playwright_locator_operation', tab_id: this.tab.numericId,
      descriptor: this.descriptor, operation, payload
    })
    return asObject(result).value
  }

  async count() {
    return (await this.matches()).length
  }

  async all() {
    const matches = await this.matches()
    return matches.map((match, index) => this.withDescriptor({ ...this.descriptor, index: Number(match.index ?? index) }))
  }

  async allTextContents() {
    return (await this.matches()).map((match) => String(match.text ?? match.visibleText ?? ''))
  }

  async textContent() {
    return String(await this.locatorOperation('textContent') ?? '')
  }

  async innerText() {
    return String(await this.locatorOperation('innerText') ?? '')
  }

  async getAttribute(name) {
    return await this.locatorOperation('getAttribute', { name: String(name ?? '') })
  }

  async isVisible() {
    return (await this.matches()).some((match) => match.visible === true)
  }

  async isEnabled() {
    return Boolean(await this.locatorOperation('isEnabled'))
  }

  async waitFor(options = {}) {
    const state = String(options.state ?? 'visible')
    if (!['attached', 'visible', 'hidden', 'detached'].includes(state)) {
      throw new Error(`Unsupported locator.waitFor state: ${state}. Use attached, visible, hidden, or detached.`)
    }
    const deadline = Date.now() + Math.max(1, Math.min(Number(options.timeoutMs ?? options.timeout ?? 30000) || 30000, 120000))
    for (;;) {
      const matches = await this.matches()
      const visibleCount = matches.filter((match) => match.visible === true).length
      if (state === 'attached' && matches.length > 0) return
      if (state === 'visible' && visibleCount > 0) return
      if (state === 'hidden' && visibleCount === 0) return
      if (state === 'detached' && matches.length === 0) return
      if (Date.now() > deadline) throw new Error(`CommandTimeout: locator.waitFor timed out waiting for ${this.selector} to be ${state}`)
      await sleep(100)
    }
  }

  async point() {
    const match = await this.strictMatch()
    if (match.visible === false || match.enabled === false) {
      throw new Error(`Locator ${this.selector} resolved to an element that is not actionable.`)
    }
    const box = asObject(match.boundingBox)
    const width = Number(box.width)
    const height = Number(box.height)
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
      throw new Error(`Locator ${this.selector} resolved to an element without a clickable bounding box.`)
    }
    return {
      x: Math.max(0, Math.round(Number(box.x ?? 0) + width / 2)),
      y: Math.max(0, Math.round(Number(box.y ?? 0) + height / 2))
    }
  }

  async click() {
    const point = await this.point()
    await dispatchClick(this.tab, point, 1)
  }

  async dblclick() {
    const point = await this.point()
    await dispatchClick(this.tab, point, 2)
  }

  async fill(value) {
    await this.click()
    await this.locatorOperation('fill', { value: String(value ?? '') })
  }

  async type(value) {
    await this.click()
    await sendText(this.tab, String(value ?? ''))
  }

  async press(key) {
    await this.click()
    await dispatchKey(this.tab, String(key ?? ''))
  }

  async check() { await this.setChecked(true) }
  async uncheck() { await this.setChecked(false) }
  async setChecked(checked) {
    await this.locatorOperation('setChecked', { checked: checked === true })
  }

  async selectOption(value) {
    const values = (Array.isArray(value) ? value : [value]).map((item) => {
      if (item && typeof item === 'object') {
        const obj = asObject(item)
        return {
          value: obj.value == null ? undefined : String(obj.value),
          label: obj.label == null ? undefined : String(obj.label),
          index: Number.isFinite(Number(obj.index)) ? Number(obj.index) : undefined
        }
      }
      return { value: String(item ?? '') }
    })
    await this.locatorOperation('selectOption', { values })
  }

  async downloadMedia() {
    throw new Error(`locator.downloadMedia failed for selector ${this.selector}: ${unsupportedDownload(this.tab.browser.name)}`)
  }

  getByText(text, options = {}) { return this.scoped({ kind: 'text', value: String(text), exact: options.exact === true }) }
  getByRole(role, options = {}) { return this.scoped({ kind: 'role', value: String(role), name: options.name, exact: options.exact === true }) }
  getByLabel(text, options = {}) { return this.scoped({ kind: 'label', value: String(text), exact: options.exact === true }) }
  getByPlaceholder(text, options = {}) { return this.scoped({ kind: 'placeholder', value: String(text), exact: options.exact === true }) }
  getByTestId(testId) { return this.scoped({ kind: 'testId', value: String(testId) }) }
  locator(selector, options = {}) { return this.scoped(withLocatorOptions({ kind: 'css', value: String(selector) }, options)) }
  filter(options = {}) { return this.withDescriptor(withLocatorOptions(this.descriptor, options)) }
  and(other) {
    this.assertSameFrameLocator(other)
    return this.withDescriptor({
      kind: 'and',
      left: this.descriptor,
      right: other.descriptor,
      frameSelectors: this.descriptor.frameSelectors
    })
  }
  or(other) {
    this.assertSameFrameLocator(other)
    return this.withDescriptor({
      kind: 'or',
      left: this.descriptor,
      right: other.descriptor,
      frameSelectors: this.descriptor.frameSelectors
    })
  }
  first() { return this.withDescriptor({ ...this.descriptor, index: 0 }) }
  last() { return this.withDescriptor({ ...this.descriptor, index: -1 }) }
  nth(index) { return this.withDescriptor({ ...this.descriptor, index: Math.trunc(Number(index)) }) }
  describeApi() { return ['count()', 'all()', 'filter(options)', 'and(locator)', 'or(locator)', 'click(options?)', 'dblclick(options?)', 'fill(value, options?)', 'type(value, options?)', 'press(key, options?)', 'innerText(options?)', 'textContent(options?)', 'getAttribute(name, options?)', 'isVisible()', 'isEnabled()', 'waitFor({ state, timeoutMs })', 'allTextContents(options?)', 'check(options?)', 'uncheck(options?)', 'setChecked(checked, options?)', 'selectOption(value, options?)', 'downloadMedia() unsupported'] }
}

async function dispatchClick(tab, point, clickCount) {
  await tab.api.moveMouse({ tabId: tab.numericId, x: point.x, y: point.y })
  await tab.cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x: point.x, y: point.y, button: 'left', clickCount })
  await tab.cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x: point.x, y: point.y, button: 'left', clickCount })
}

async function dispatchKey(tab, key) {
  await tab.cdp('Input.dispatchKeyEvent', { type: 'keyDown', key })
  await tab.cdp('Input.dispatchKeyEvent', { type: 'keyUp', key })
}

async function sendText(tab, text) {
  for (const char of String(text ?? '')) {
    await tab.cdp('Input.dispatchKeyEvent', { type: 'char', text: char, key: char })
  }
}

function finiteNumberOrDefault(value, fallback, name) {
  if (value == null) return fallback
  const numeric = Number(value)
  if (!Number.isFinite(numeric)) throw new Error(`InvalidArgument: ${name} must be a finite number.`)
  return numeric
}

function scrollDistance(options = {}, useXYAsDistance = false) {
  return {
    scrollX: finiteNumberOrDefault(options.scrollX ?? options.deltaX ?? (useXYAsDistance ? options.x : undefined), 0, 'scrollX'),
    scrollY: finiteNumberOrDefault(options.scrollY ?? options.deltaY ?? (useXYAsDistance ? options.y : undefined), 0, 'scrollY')
  }
}

function assertScrollDistance(distance) {
  if (distance.scrollX === 0 && distance.scrollY === 0) {
    throw new Error('InvalidArgument: Scroll requires a non-zero distance. For CUA use scrollX/scrollY or deltaX/deltaY; for DOM-CUA page scroll use { y: 700 }.')
  }
}

async function viewportCenter(tab) {
  const metrics = asObject(await tab.cdp('Page.getLayoutMetrics').catch(() => ({})))
  const viewport = asObject(metrics.cssVisualViewport ?? metrics.visualViewport)
  const width = finiteNumberOrDefault(viewport.clientWidth ?? viewport.width, tab.browser.info?.viewportWidth ?? 900, 'viewportWidth')
  const height = finiteNumberOrDefault(viewport.clientHeight ?? viewport.height, tab.browser.info?.viewportHeight ?? 640, 'viewportHeight')
  return { x: Math.max(0, Math.round(width / 2)), y: Math.max(0, Math.round(height / 2)) }
}

class CuaApi {
  constructor(tab) { this.tab = tab }
  async move(options = {}) {
    await this.tab.api.moveMouse({ tabId: this.tab.numericId, x: Number(options.x), y: Number(options.y), waitForArrival: options.waitForArrival !== false })
  }
  async click(options = {}) {
    const point = { x: Number(options.x), y: Number(options.y) }
    await dispatchClick(this.tab, point, 1)
  }
  async double_click(options = {}) {
    const point = { x: Number(options.x), y: Number(options.y) }
    await dispatchClick(this.tab, point, 2)
  }
  async drag(options = {}) {
    const path = asArray(options.path)
    for (const point of path) await this.move(point)
    const last = asObject(path.at(-1))
    if (last.x != null && last.y != null) await this.tab.cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x: Number(last.x), y: Number(last.y), button: 'left' })
  }
  async type(options = {}) { await sendText(this.tab, typeof options === 'string' ? options : String(options.text ?? '')) }
  async keypress(options = {}) {
    const keys = typeof options === 'string' ? [options] : Array.isArray(options) ? options : asArray(options.keys ?? options.key)
    for (const key of keys.map(String)) await dispatchKey(this.tab, key)
  }
  async scroll(options = {}) {
    const x = Number(options.x ?? 0)
    const y = Number(options.y ?? 0)
    const distance = scrollDistance(options, false)
    assertScrollDistance(distance)
    await this.move({ x, y })
    await this.tab.cdp('Input.synthesizeScrollGesture', {
      x,
      y,
      xDistance: -distance.scrollX,
      yDistance: -distance.scrollY,
      gestureSourceType: 'mouse',
      preventFling: true,
      speed: 8000
    })
  }
  async get_visible_screenshot() { return await this.tab.screenshot() }
  async download_media() { throw new Error(unsupportedDownload(this.tab.browser.name)) }
  describeApi() { return ['move({ x, y })', 'click({ x, y })', 'double_click({ x, y })', 'drag({ path })', 'type({ text })', 'keypress({ keys })', 'scroll({ x, y, scrollX?, scrollY?, deltaX?, deltaY? })', 'download_media() unsupported'] }
}

class DomCuaApi {
  constructor(tab) {
    this.tab = tab
  }
  async get_visible_dom() {
    const snapshot = JSON.parse(await this.tab.playwright.domSnapshot())
    return asArray(snapshot.elements).map((element) => {
      const nodeId = element.node_id ?? element.backendNodeId ?? element.ref
      return `node_id=${nodeId} role=${element.role ?? ''} name=${JSON.stringify(element.name ?? element.text ?? '')} selector=${JSON.stringify(element.selector ?? '')}`
    }).join('\n')
  }
  async pointFor(options = {}) {
    const nodeId = options.node_id ?? options.nodeId
    if (!nodeId) throw new Error('InvalidArgument: DOM CUA action requires node_id from get_visible_dom().')
    const info = asObject(await this.tab.api.executeUnhandledCommand({ type: 'dom_cua_node_info', tab_id: this.tab.numericId, node_id: String(nodeId) }))
    if (info.visible !== true || info.enabled !== true) throw new Error('Element is not actionable.')
    const box = asObject(info.boundingBox)
    return { x: Number(box.x) + Number(box.width) / 2, y: Number(box.y) + Number(box.height) / 2 }
  }
  async click(options = {}) { await dispatchClick(this.tab, await this.pointFor(options), 1) }
  async double_click(options = {}) { await dispatchClick(this.tab, await this.pointFor(options), 2) }
  async type(options = {}) {
    if (asObject(options).node_id) await this.click(options)
    await sendText(this.tab, typeof options === 'string' ? options : String(options.text ?? ''))
  }
  async keypress(options = {}) {
    if (asObject(options).node_id) await this.click(options)
    const keys = typeof options === 'string' ? [options] : Array.isArray(options) ? options : asArray(options.keys ?? options.key)
    for (const key of keys.map(String)) await dispatchKey(this.tab, key)
  }
  async scroll(options = {}) {
    const point = asObject(options).node_id ? await this.pointFor(options) : await viewportCenter(this.tab)
    const distance = scrollDistance(options, true)
    assertScrollDistance(distance)
    await this.tab.api.moveMouse({ tabId: this.tab.numericId, x: point.x, y: point.y })
    await this.tab.cdp('Input.synthesizeScrollGesture', {
      x: point.x,
      y: point.y,
      xDistance: -distance.scrollX,
      yDistance: -distance.scrollY,
      gestureSourceType: 'mouse',
      preventFling: true,
      speed: 8000
    })
  }
  async downloadMedia() { throw new Error(unsupportedDownload(this.tab.browser.name)) }
  async download_media() { return await this.downloadMedia() }
  describeApi() { return ['get_visible_dom()', 'click({ node_id })', 'double_click({ node_id })', 'type({ node_id?, text })', 'keypress({ node_id?, key|keys })', 'scroll({ node_id?, x?, y?, scrollX?, scrollY?, deltaX?, deltaY? })', 'downloadMedia() unsupported'] }
}

class ClipboardApi {
  constructor(tab) { this.tab = tab }
  async writeText(text) {
    await this.tab.api.executeUnhandledCommand({
      type: 'tab_clipboard_write_text',
      tab_id: this.tab.numericId,
      text: String(text ?? '')
    })
  }
  async readText() {
    const result = asObject(await this.tab.api.executeUnhandledCommand({
      type: 'tab_clipboard_read_text',
      tab_id: this.tab.numericId
    }))
    return String(result.text ?? '')
  }
  async write(items) {
    await this.tab.api.executeUnhandledCommand({
      type: 'tab_clipboard_write',
      tab_id: this.tab.numericId,
      items: normalizeClipboardItems(items)
    })
  }
  async read() {
    const data = asObject(await this.tab.api.executeUnhandledCommand({
      type: 'tab_clipboard_read',
      tab_id: this.tab.numericId
    }))
    return asArray(data.items).map((item) => ({
      entries: asArray(asObject(item).entries).map((entry) => ({
        mimeType: entry.mimeType ?? entry.mime_type,
        text: entry.text,
        base64: entry.base64
      })),
      presentationStyle: asObject(item).presentationStyle ?? asObject(item).presentation_style
    }))
  }
  describeApi() { return ['readText()', 'writeText(text)', 'read()', 'write(items)'] }
}

function normalizeClipboardItems(items) {
  return asArray(items).map((item) => ({
    entries: asArray(asObject(item).entries).map((entry) => ({
      mime_type: entry.mime_type ?? entry.mimeType,
      text: entry.text,
      base64: entry.base64
    })),
    presentation_style: asObject(item).presentation_style ?? asObject(item).presentationStyle ?? 'unspecified'
  }))
}

class PageAssetsCapability {
  constructor(tab) {
    this.tab = tab
    this.inventories = new Map()
  }
  async list() {
    const resources = asArray(await this.tab.eval('(() => performance.getEntriesByType("resource").map((entry) => ({ name: entry.name, initiatorType: entry.initiatorType })))()'))
    const inlineSvgs = asArray(await this.tab.eval('(() => Array.from(document.querySelectorAll("svg")).map((node) => ({ markup: node.outerHTML, name: node.getAttribute("aria-label") || node.id || "inline-svg" })))()'))
    await this.tab.cdp('DOMSnapshot.captureSnapshot', { computedStyles: [] }).catch(() => ({}))
    const assets = resources.map((entry, index) => {
      const kind = String(asObject(entry).initiatorType ?? '').toLowerCase() === 'css' ? 'stylesheet' : 'other'
      const url = String(asObject(entry).name ?? '')
      return { id: `${kind}-${index + 1}`, kind, name: path.basename(url) || kind, url, sources: [{ kind: 'resource', property: String(asObject(entry).initiatorType ?? 'resource') }] }
    }).filter((asset) => asset.url)
    const byKind = {}
    for (const asset of assets) byKind[asset.kind] = (byKind[asset.kind] ?? 0) + 1
    const inventory = {
      id: `page-assets-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`,
      assets,
      inlineSvgs: inlineSvgs.map((svg, index) => ({ id: `inline-svg-${index + 1}`, ...asObject(svg) })),
      pageUrl: await this.tab.url(),
      summary: { byKind, inlineSvgCount: inlineSvgs.length, totalCount: assets.length }
    }
    this.inventories.set(inventory.id, inventory)
    return inventory
  }
  async mainFrameId() {
    const tree = asObject(asObject(await this.tab.cdp('Page.getFrameTree')).frameTree)
    const frame = asObject(tree.frame)
    const id = typeof frame.id === 'string' ? frame.id : ''
    if (!id) throw new Error('Page.getFrameTree did not return a main frame id.')
    return id
  }
  async bundle(options = {}) {
    const inventory = this.inventories.get(String(options.inventoryId ?? ''))
    if (!inventory) throw new Error('pageAssets.bundle requires inventoryId from a prior pageAssets.list() result.')
    if (this.tab.browser.globals?.nodeRepl?.createElicitation) {
      const response = await this.tab.browser.globals.nodeRepl.createElicitation({
        message: 'Allow DotCraft to bundle page assets from the current browser page?',
        meta: { file_transfer: 'download' }
      })
      if (asObject(response).action === 'decline') throw new Error('ApprovalDenied: pageAssets.bundle was declined.')
    }
    const kindFilter = new Set(asArray(options.kinds).map(String))
    const idFilter = new Set(asArray(options.assetIds).map(String))
    const requested = inventory.assets.filter((asset) => {
      if (kindFilter.size && !kindFilter.has(asset.kind)) return false
      if (idFilter.size && !idFilter.has(asset.id)) return false
      return true
    })
    const directoryPath = await mkdtemp(path.join(tmpdir(), 'dotcraft-page-assets-'))
    await mkdir(directoryPath, { recursive: true })
    const assets = []
    const failures = []
    const frameId = await this.mainFrameId()
    for (const asset of requested) {
      try {
        const content = asObject(await this.tab.cdp('Page.getResourceContent', { frameId, url: asset.url }))
        const buffer = content.base64Encoded ? Buffer.from(String(content.content ?? ''), 'base64') : Buffer.from(String(content.content ?? ''), 'utf8')
        const fileName = `${asset.id}-${sanitizeFileName(asset.name || 'asset')}`
        const filePath = path.join(directoryPath, fileName)
        await writeFile(filePath, buffer)
        assets.push({ ...asset, path: filePath, name: fileName, contentType: asset.kind === 'stylesheet' ? 'text/css' : null })
      } catch (error) {
        failures.push({ ...asset, reason: error instanceof Error ? error.message : String(error) })
      }
    }
    const summary = { requestedCount: requested.length, downloadedCount: assets.length, failedCount: failures.length }
    const manifestPath = path.join(directoryPath, 'manifest.json')
    await writeFile(manifestPath, JSON.stringify({ inventoryId: inventory.id, assets, failures, summary }, null, 2), 'utf8')
    return { directoryPath, manifestPath, assets, failures, summary }
  }
  describeApi() { return ['list()', 'bundle({ inventoryId, kinds?, assetIds? })'] }
}

function sanitizeFileName(value) {
  return String(value ?? 'asset').replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-').replace(/\s+/g, '-').slice(0, 80) || 'asset'
}

class WebMcpCapability {
  constructor(tab) { this.tab = tab }
  async listTools() {
    if (!await this.tab.isWebMcpAvailable()) throw new Error(WEBMCP_UNAVAILABLE_ERROR)
    const tools = asArray(await this.tab.eval(`(() => {
      const modelContext = typeof navigator !== "undefined" ? navigator.modelContext : undefined;
      if (!modelContext || typeof modelContext.getTools !== "function" || typeof modelContext.executeTool !== "function") {
        throw new Error(${jsString(WEBMCP_UNAVAILABLE_ERROR)});
      }
      return Promise.resolve(modelContext.getTools()).then((tools) => tools);
    })()`))
    return tools.map((tool) => {
      const raw = asObject(tool)
      const inputSchema = typeof raw.input_schema === 'string' ? JSON.parse(raw.input_schema) : asObject(raw.inputSchema)
      return {
        name: raw.name,
        title: raw.title,
        description: raw.description,
        inputSchema,
        annotations: raw.annotations,
        origin: raw.origin,
        pageUrl: raw.pageUrl,
        invoke: async (input, options = {}) => await this.invokeTool({ toolName: raw.name, input, timeoutMs: options.timeoutMs })
      }
    })
  }
  async invokeTool(options = {}) {
    const toolName = String(options.toolName ?? '').trim()
    if (!toolName) throw new Error('tab.capabilities.webmcp.invokeTool requires a toolName')
    if (!await this.tab.isWebMcpAvailable()) throw new Error(WEBMCP_UNAVAILABLE_ERROR)
    const inputJson = JSON.stringify(options.input ?? null)
    return await this.tab.eval(`(() => {
      const modelContext = typeof navigator !== "undefined" ? navigator.modelContext : undefined;
      if (!modelContext || typeof modelContext.getTools !== "function" || typeof modelContext.executeTool !== "function") {
        throw new Error(${jsString(WEBMCP_UNAVAILABLE_ERROR)});
      }
      return Promise.resolve(modelContext.getTools()).then((tools) => {
        const tool = Array.from(tools || []).find((candidate) => candidate && candidate.name === ${jsString(toolName)});
        if (!tool) throw new Error(${jsString(`WebMCP tool not found: ${toolName}`)});
        return modelContext.executeTool(tool, ${jsString(inputJson)});
      }).then((result) => {
        if (result == null) return null;
        try { return JSON.parse(result); } catch { return result; }
      });
    })()`, options.timeoutMs)
  }
  describeApi() { return ['listTools()', 'invokeTool({ toolName, input?, timeoutMs? })'] }
}

export async function setupBrowserRuntime(options = {}) {
  const globals = options.globals ?? globalThis
  if (!globals.nodeRepl?.nativePipe?.createConnection) {
    throw new Error('privileged native pipe bridge is not available; DotCraft browser client is not trusted')
  }
  const getTurnMetadata = () => turnMetadata(globals)
  const discovered = await discoverIabBackends(globals, getTurnMetadata)
  if (discovered.length === 0) {
    throw new Error('IabBackendUnavailable: failed to discover DotCraft Desktop IAB backend.')
  }
  const browsers = discovered.map(({ api, info }) => new BrowserHandle(api, info, globals))
  const requested = options.backend ? String(options.backend) : null
  const available = requested ? browsers.filter((browser) => browser.id === requested || browser.info.type === requested) : browsers
  if (available.length === 0) throw new Error(`Browser backend not found: ${requested}`)
  const agent = {}
  agent.browsers = {
    list: async () => available.map((browser) => ({
      id: browser.id,
      name: browser.name,
      type: browser.info.type,
      metadata: browser.info.metadata,
      capabilities: browser.info.capabilities
    })),
    get: async (id) => {
      const key = String(id ?? '')
      const found = available.find((browser) => browser.id === key || browser.info.type === key || (key === 'browser' && browser.id === 'iab'))
      if (!found) throw new Error(`Browser not found: ${id}. Available browser id: iab.`)
      return found
    },
    describeApi: () => ['list()', 'get("iab")']
  }
  agent.documentation = { get: async (name) => browserTopic(available[0].info, String(name)) }
  agent.browser = available[0]
  return agent
}
