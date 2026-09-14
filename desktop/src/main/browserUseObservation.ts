declare const window: any
declare const document: any
declare const location: any
type Element = any
type HTMLElement = any
type HTMLIFrameElement = any
type Document = any
type ShadowRoot = any

export function browserObservationSource(playwrightSource: string): string {
  return `(() => {
    const module = { exports: {} };
    ${playwrightSource}
    const injected = new (module.exports.InjectedScript())(globalThis, {
      isUnderTest: false, sdkLanguage: 'javascript', testIdAttributeName: 'data-testid',
      stableRafCount: 2, browserName: 'chromium', isUtilityWorld: false, customEngines: []
    });
    (${installObservation.toString()})(injected, { getAriaRole, getElementAccessibleName });
    return true;
  })()`
}

// Serialized into the page; all dependencies are passed explicitly.
function installObservation(injected: any, semantics: any): void {
  const host = window as any
  const nodes = new Map<string, Element>()
  let generation = 0
  const normalize = (value: unknown): string => String(value ?? '').replace(/\s+/g, ' ').trim()
  const state = (element: Element, name: string): boolean => injected.elementState(element, name).matches
  const box = (element: Element): { x: number; y: number; width: number; height: number } => {
    const rect = element.getBoundingClientRect()
    let x = rect.left
    let y = rect.top
    let view = element.ownerDocument.defaultView
    while (view && view !== window) {
      const frame = view.frameElement as HTMLElement | null
      if (!frame) break
      const bounds = frame.getBoundingClientRect()
      x += bounds.left + frame.clientLeft
      y += bounds.top + frame.clientTop
      view = frame.ownerDocument.defaultView
    }
    return { x, y, width: rect.width, height: rect.height }
  }
  const visible = (element: Element): boolean => {
    if (!state(element, 'visible')) return false
    let view = element.ownerDocument.defaultView
    while (view && view !== window) {
      const frame = view.frameElement
      if (!frame || !state(frame, 'visible')) return false
      view = frame.ownerDocument.defaultView
    }
    return true
  }
  const info = (element: Element, index: number): any => {
    const tagName = element.tagName.toLowerCase()
    const name = semantics.getElementAccessibleName(element, false)
    const text = normalize((element as HTMLElement).innerText ?? element.textContent)
    let selector = tagName
    try { selector = injected.generateSelectorSimple(element) || tagName } catch { /* Detached elements have no selector. */ }
    return {
      index, tagName, tag: tagName, role: semantics.getAriaRole(element) || '', name,
      text, visibleText: text, ariaName: name, selector, visible: visible(element),
      enabled: state(element, 'enabled'), boundingBox: box(element),
      href: element.getAttribute('href') ?? undefined, testId: element.getAttribute('data-testid') ?? undefined,
      attributes: Object.fromEntries(Array.from(element.attributes).map((attribute: any) => [attribute.name, attribute.value]))
    }
  }
  const matcher = (text: string, input: any): boolean => {
    if (input?.pattern != null) return new RegExp(input.pattern, input.flags).test(text)
    const value = normalize(input?.value ?? input)
    return input?.exact ? normalize(text) === value : normalize(text).toLowerCase().includes(value.toLowerCase())
  }
  const quoted = (value: unknown, exact: boolean): string => `${JSON.stringify(String(value ?? ''))}${exact ? 's' : 'i'}`
  const query = (selector: string, root: Document | Element): Element[] => injected.querySelectorAll(injected.parseSelector(selector), root)
  const resolve = (descriptor: any, root: Document | Element = document, frames = true): Element[] => {
    if (frames) {
      for (const selector of descriptor.frameSelectors ?? []) {
        const matches = query(selector, root)
        if (matches.length !== 1) throw new Error('Strict mode violation: frameLocator must resolve to one frame.')
        const frame = matches[0] as HTMLIFrameElement
        if (!frame.contentDocument) throw new Error('UnsupportedApi: cross-origin frameLocator is not supported.')
        root = frame.contentDocument
      }
    }
    let result: Element[]
    switch (descriptor.kind) {
      case 'chain': result = resolve(descriptor.parent, root, false).flatMap(parent => resolve(descriptor.child, parent, false)); break
      case 'and': { const right = new Set(resolve(descriptor.right, root, false)); result = resolve(descriptor.left, root, false).filter(element => right.has(element)); break }
      case 'or': result = [...new Set([...resolve(descriptor.left, root, false), ...resolve(descriptor.right, root, false)])]; break
      case 'role': result = query(`internal:role=${descriptor.value}${descriptor.name != null ? `[name=${quoted(descriptor.name, descriptor.exact)}]` : ''}`, root); break
      case 'text': result = query(`internal:text=${quoted(descriptor.value, descriptor.exact)}`, root); break
      case 'label': result = query(`internal:label=${quoted(descriptor.value, descriptor.exact)}`, root); break
      case 'placeholder': result = query(`internal:attr=[placeholder=${quoted(descriptor.value, descriptor.exact)}]`, root); break
      case 'testId': result = query(`internal:testid=[data-testid=${quoted(descriptor.value, true)}]`, root); break
      default: result = query(descriptor.value || '*', root)
    }
    for (const filter of descriptor.filters ?? []) {
      if (filter.kind === 'has' || filter.kind === 'hasNot') result = result.filter(element => (resolve(filter.descriptor, element, false).length > 0) === (filter.kind === 'has'))
      else if (filter.kind === 'hasText' || filter.kind === 'hasNotText') result = result.filter(element => matcher(element.textContent || '', filter.matcher ?? filter.value) === (filter.kind === 'hasText'))
      else if (filter.kind === 'visible') result = result.filter(element => visible(element) === filter.value)
    }
    if (Number.isInteger(descriptor.index)) {
      const element = result.at(descriptor.index)
      result = element ? [element] : []
    }
    return [...new Set(result)]
  }
  const snapshot = (): any => {
    generation++
    nodes.clear()
    const elements: any[] = []
    const aria: string[] = []
    const visit = (root: Document | ShadowRoot): void => {
      const body = root.nodeType === 9 ? (root as Document).body : null
      if (body) aria.push(injected.ariaSnapshot(body, { mode: 'default' }))
      for (const element of Array.from(root.querySelectorAll('*')) as Element[]) {
        if (!visible(element)) continue
        const role = semantics.getAriaRole(element)
        if (role && !['generic', 'none', 'presentation'].includes(role) && elements.length < 200) {
          const ref = `s${generation}e${elements.length + 1}`
          nodes.set(ref, element)
          elements.push({ ...info(element, elements.length), ref, node_id: ref })
        }
        if (element.shadowRoot) visit(element.shadowRoot)
        if (element.tagName === 'IFRAME') {
          const frame = element as HTMLIFrameElement
          if (frame.contentDocument) visit(frame.contentDocument)
        }
      }
    }
    visit(document)
    return { title: document.title, url: location.href, bodyText: normalize(document.body?.innerText).slice(0, 4000), accessibilitySnapshot: aria.join('\n'), elements }
  }
  const operation = (descriptor: any, action: string, payload: any = {}): any => {
    const elements = resolve(descriptor)
    if (action === 'resolve') return elements.map(info)
    if (elements.length !== 1) throw new Error(`Strict mode violation: locator resolved to ${elements.length} elements.`)
    const element = elements[0] as HTMLElement
    switch (action) {
      case 'textContent': return element.textContent
      case 'innerText': return element.innerText
      case 'getAttribute': return element.getAttribute(payload.name)
      case 'isEnabled': return state(element, 'enabled')
      case 'fill': {
        if (!state(element, 'enabled')) throw new Error('Element is disabled.')
        const result = injected.fill(element, payload.value)
        if (result !== 'done' && result !== 'needsinput') throw new Error(String(result))
        return { needsInput: result === 'needsinput' }
      }
      case 'selectOption': return injected.selectOptions(element, payload.values)
      case 'setChecked': {
        if (!state(element, 'enabled')) throw new Error('Element is disabled.')
        if (state(element, 'checked') !== payload.checked) element.click()
        return null
      }
      default: throw new Error(`UnsupportedApi: locator operation ${action}`)
    }
  }
  host.__dotcraftPlaywrightInjected = injected
  host.__dotcraftBrowserUseSnapshot = snapshot
  host.__dotcraftBrowserUseElementInfo = info
  host.__dotcraftBrowserUseResolveSelector = (parsed: any) => injected.querySelectorAll(parsed, document).map(info)
  host.__dotcraftBrowserUseLocator = operation
  host.__dotcraftBrowserUseNode = (id: string) => {
    const element = nodes.get(id)
    if (!element?.isConnected) throw new Error('NodeStale: take a fresh visible DOM snapshot.')
    element.scrollIntoView({ block: 'center', inline: 'center' })
    return info(element, 0)
  }
}
