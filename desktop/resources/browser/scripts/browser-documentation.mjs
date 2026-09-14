const topics = [
  {
    name: 'tabs', supported: 'tabs',
    text: `# Tabs and turn cleanup
browser.tabs.new(url?), selected(), list(), get(id), content({ urls, contentType }), and finalize({ keep }) manage this task's tabs.
list() and browser.user.openTabs() return TabInfo records. Use tabs.get(info.id) or browser.user.claimTab(info) for a live handle.
selected() returns undefined when there is no selected tab. Reuse existing browser and tab handles across evaluations.
Agent-created tabs are temporary: completed, failed, and cancelled turns close unmarked tabs. User tabs are released without closing.
Call tab.markDeliverable() for a user-facing result or tab.markHandoff() for work continuing in a later turn. Marks belong to the active turn; the latest mark wins. Deliverables are released as user pages. Handoff pages remain temporary and need another mark in the next browser-using turn; ordinary chat turns do not clean them up.
For a requested page output, acquire a tab, await tab.markDeliverable(), set visibility, then navigate. Visibility alone does not retain a page.
After release, discard the old tab handle and find the live page with browser.user.openTabs(), then browser.user.claimTab(info). Reuse the browser connection without reloading the page. Only a REPL/kernel reset requires bootstrap again; an empty tab list or stale tab does not.
Explicit browser.tabs.finalize({ keep: [{ tab, status: "deliverable" | "handoff" }] }) remains available for early cleanup. An evaluation ending does not close tabs.
tab.goto(url), back(), forward(), reload(), close(), title(), and url() provide navigation. Do not navigate again when already on the intended page.`
  },
  {
    name: 'observation', supported: 'playwrightCommonSubset',
    text: `# Observation and locators
await tab.playwright.domSnapshot() returns a JSON string with title, url, bodyText, accessibilitySnapshot, and elements. Parse it only when structured fields are needed.
Snapshots report actual accessible names, visible elements, and enabled states, including open Shadow DOM and supported same-origin frames. Cross-origin frame interaction is unsupported.
Use tab.playwright.getByRole(role, { name, exact }), getByText(text, { exact }), getByLabel(text, { exact }), getByPlaceholder(text, { exact }), getByTestId(id), locator(selector), and frameLocator(selector).
Locators support count(), all(), first(), last(), nth(index), filter({ has, hasNot, hasText, hasNotText, visible }), and()/or(), and scoped locators.
Reads: allTextContents(), textContent(), innerText(), getAttribute(name), isVisible(), isEnabled(). Actions: click(), dblclick(), fill(text), type(text), press(key), check(), uncheck(), setChecked(boolean), selectOption(value).
waitFor({ state: "attached" | "detached" | "visible" | "hidden", timeoutMs }) waits for locator state. Page waits: waitForURL(url, options), waitForLoadState(stateOrOptions), expectNavigation(action, options).
tab.playwright.evaluate(fnOrExpression, arg?, { timeoutMs }) is read-only. Use locator, CUA or DOM-CUA actions for changes.
After an ambiguous locator or timeout, inspect fresh state and refine the locator rather than retrying unchanged. Prefer the cheapest observation that answers the next question.`
  },
  {
    name: 'dom-cua', supported: 'domCua',
    text: `# DOM-CUA and coordinate actions
await tab.dom_cua.get_visible_dom() returns visible nodes with opaque node_id strings. Do not derive ids or treat them as CDP backend node numbers.
Use tab.dom_cua.click({ node_id }), double_click({ node_id }), type({ node_id?, text }), keypress({ node_id?, keys }), or scroll({ node_id?, y: 700 }). Refresh visible DOM after navigation or UI replacement.
Coordinate actions use tab.cua.click({ x, y }), double_click({ x, y }), move({ x, y }), drag({ path: [{ x, y }, ...] }), type({ text }), and keypress({ keys }).
tab.cua.scroll({ x, y, scrollY: 700 }) uses viewport coordinates for x/y and distance for scrollY. Zero-distance scrolling is not an action.`
  },
  {
    name: 'screenshots', supported: 'basicNavigation',
    text: `# Screenshots and local testing
await nodeRepl.emitImage(await tab.screenshot({ fullPage: false })) displays the page. screenshot({ fullPage: true }) or screenshot({ clip: { x, y, width, height } }) can capture a larger page or region.
For local web development, reload after code/build changes when hot reload is unavailable, then take a fresh snapshot or screenshot.
tab.dev.logs({ filter?, levels?, limit? }) reads captured console logs. tab.clipboard supports readText(), writeText(text), read(), and write(items) using the virtual clipboard.`
  },
  {
    name: 'visibility', browserCapability: 'visibility',
    text: `# Visibility
Keep browser work in the background unless the user asks to see or watch the page.
const visibility = await browser.capabilities.get("visibility"); await visibility.set(true);
visibility.get() reports visibility; visibility.set(false) hides the page. Always await/cache a capability handle before calling its methods.`
  },
  {
    name: 'viewport', browserCapability: 'viewport',
    text: `# Viewport
const viewport = await browser.capabilities.get("viewport"); await viewport.set({ width: 1280, height: 720 });
await viewport.reset() restores the normal viewer viewport. Use this for responsive-layout verification.`
  },
  {
    name: 'pageAssets', tabCapability: 'pageAssets',
    text: `# Page assets
const assets = await tab.capabilities.get("pageAssets"); const inventory = await assets.list();
await assets.bundle({ inventoryId: inventory.id, kinds: ["image", "font", "stylesheet"] }) writes assets and a manifest to temporary output through the existing file-transfer approval path.
This is not ordinary download, upload, or filechooser support.`
  },
  {
    name: 'webmcp', supported: 'webmcp',
    text: `# Page-defined WebMCP tools
First call await tab.capabilities.list(). Only when that current-page result includes webmcp may you get the capability.
const webmcp = await tab.capabilities.get("webmcp"); const tools = await webmcp.listTools();
await webmcp.invokeTool({ toolName, input, timeoutMs }) invokes a tool explicitly exposed by navigator.modelContext. Navigation can change availability. Browser documentation alone does not establish page tool availability.`
  }
]

function applicableTopics(info) {
  const capabilities = info?.capabilities ?? {}
  const supported = new Set(capabilities.docs?.supported ?? [])
  const browserIds = new Set((capabilities.browser ?? []).map(item => item.id))
  const tabIds = new Set((capabilities.tab ?? []).map(item => item.id))
  return topics.filter(topic => (!topic.supported || supported.has(topic.supported))
    && (!topic.browserCapability || browserIds.has(topic.browserCapability))
    && (!topic.tabCapability || tabIds.has(topic.tabCapability)))
}

export function browserDocumentation(info) {
  const available = applicableTopics(info)
  return `# ${info.name || 'DotCraft In-App Browser'}\n\n${available.filter(topic => ['tabs', 'observation', 'dom-cua'].includes(topic.name)).map(topic => topic.text).join('\n\n')}\n\n## Named documentation\n${available.map(topic => `- agent.documentation.get(${JSON.stringify(topic.name)})`).join('\n')}\n\nOnly the documented IAB subset is supported. Ordinary agent downloads, uploads, filechooser, hidden browsing history, raw CDP capabilities, AX APIs, and cross-origin frame interaction are unavailable.`
}

export function browserTopic(info, name) {
  const available = applicableTopics(info)
  const topic = available.find(topic => topic.name === name)
  if (!topic) throw new Error(`Browser documentation unavailable: ${name}. Available topics: ${available.map(topic => topic.name).join(', ')}`)
  return topic.text
}
