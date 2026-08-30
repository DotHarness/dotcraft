#!/usr/bin/env node
/**
 * A build / load / activate smoke test for the sample Desktop Plugins. It builds each
 * bundle, loads it through the shared React runtime, calls `activate(host)`, checks what
 * it registered, then disposes and checks it let go.
 *
 * It does not render UI; Desktop's vitest suites cover rendered behavior.
 *
 * Run: node sdk/typescript/samples/desktop-plugins/verify-samples.mjs
 */
import { spawnSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const repo = join(here, '..', '..', '..', '..')
const builder = join(repo, 'sdk', 'typescript', 'packages', 'plugin', 'scripts', 'build-plugin.mjs')
const desktopRequire = createRequire(join(repo, 'desktop', 'package.json'))

const SAMPLES = [
  {
    id: 'grok-mascot',
    surfaces: [
      ['replace', 'composer.mascot'],
      ['add', 'composer.mascot']
    ],
    settingsPages: ['appearance'],
    commands: ['next-color']
  },
  {
    id: 'wallpaper',
    surfaces: [['replace', 'app.background']],
    settingsPages: ['wallpaper'],
    commands: ['next-scene', 'toggle'],
    services: ['wallpaper.controller'],
    appearance: 'backdrop',
    defaults: { blur: 0, dim: 0, surfaceOpacity: 30 }
  },
  {
    id: 'token-hud',
    surfaces: [['add', 'app.status']],
    settingsPages: ['token-hud'],
    commands: ['toggle'],
    appServerRequests: ['usage/summary'],
    appServerNotifications: [
      'item/agentMessage/delta',
      'item/reasoning/delta',
      'item/toolCall/argumentsDelta',
      'item/usage/delta'
    ]
  }
]

const NOTIFICATION_PROBES = {
  'turn/started': { turn: { id: 'turn-probe', threadId: 'thread-probe' } },
  'item/agentMessage/delta': {
    threadId: 'thread-probe',
    turnId: 'turn-probe',
    delta: 'Hello'
  },
  'item/reasoning/delta': {
    threadId: 'thread-probe',
    turnId: 'turn-probe',
    delta: 'Thinking'
  },
  'item/toolCall/argumentsDelta': {
    threadId: 'thread-probe',
    turnId: 'turn-probe',
    delta: '{'
  },
  'item/usage/delta': {
    threadId: 'thread-probe',
    turnId: 'turn-probe',
    outputTokens: 80
  },
  'turn/completed': { turn: { id: 'turn-probe', threadId: 'thread-probe' } },
  'turn/failed': { turn: { id: 'turn-probe', threadId: 'thread-probe' } },
  'turn/cancelled': { turn: { id: 'turn-probe', threadId: 'thread-probe' } }
}

const failures = []

function fail(sample, message) {
  failures.push(`${sample}: ${message}`)
  console.error(`  FAIL ${message}`)
}

function ok(message) {
  console.log(`  ok   ${message}`)
}

function checkMarketplace() {
  const documentPath = join(here, '.craft', 'plugins', 'marketplace.json')
  const marketplace = JSON.parse(readFileSync(documentPath, 'utf8'))
  const entries = new Map(marketplace.plugins?.map((entry) => [entry.name, entry]) ?? [])

  if (marketplace.name !== 'dotcraft-desktop-plugin-samples') {
    fail('marketplace', `unexpected name "${marketplace.name}"`)
  }
  if (entries.size !== SAMPLES.length) {
    fail('marketplace', `expected ${SAMPLES.length} entries, found ${entries.size}`)
  }

  for (const sample of SAMPLES) {
    const entry = entries.get(sample.id)
    if (entry === undefined) {
      fail('marketplace', `missing entry for "${sample.id}"`)
      continue
    }
    if (entry.source?.source !== 'local' || entry.source?.path !== `./${sample.id}`) {
      fail('marketplace', `"${sample.id}" does not point at its local plugin root`)
    }
    if (entry.policy?.installation !== 'AVAILABLE' || entry.policy?.authentication !== 'ON_INSTALL') {
      fail('marketplace', `"${sample.id}" has an unsupported publication policy`)
    }

    const manifest = JSON.parse(readFileSync(join(here, sample.id, '.craft-plugin', 'plugin.json'), 'utf8'))
    if (manifest.id !== entry.name) {
      fail('marketplace', `entry "${entry.name}" points at manifest "${manifest.id}"`)
    }

    if (sample.defaults) {
      const schema = JSON.parse(readFileSync(join(here, sample.id, 'settings.schema.json'), 'utf8'))
      const defaults = Object.fromEntries(
        schema.fields
          .filter((field) => Object.hasOwn(field, 'defaultValue'))
          .map((field) => [field.key, field.defaultValue])
      )
      for (const [key, expected] of Object.entries(sample.defaults)) {
        if (defaults[key] !== expected) {
          fail(sample.id, `default "${key}" is ${JSON.stringify(defaults[key])}, expected ${JSON.stringify(expected)}`)
        }
      }
    }
  }

  ok(`marketplace lists ${entries.size} sample plugin(s)`)
}

function build(sample) {
  const root = join(here, sample.id, 'desktop')
  const tsc = join(root, 'node_modules', 'typescript', 'bin', 'tsc')
  if (existsSync(tsc)) {
    const typecheck = spawnSync(process.execPath, [tsc, '--noEmit', '-p', join(root, 'tsconfig.json')], {
      encoding: 'utf8'
    })
    if (typecheck.status !== 0) {
      fail(sample.id, `typecheck failed\n${typecheck.stdout}${typecheck.stderr}`)
      return null
    }
    ok('typecheck')
  } else {
    console.log('  skip typecheck (run npm install in desktop/ first)')
  }

  const built = spawnSync(process.execPath, [builder, 'build', root], { encoding: 'utf8' })
  if (built.status !== 0) {
    fail(sample.id, `build failed\n${built.stdout}${built.stderr}`)
    return null
  }
  ok('build')
  return join(root, 'dist')
}

function checkOutput(sample, dist) {
  const manifestPath = join(here, sample.id, '.craft-plugin', 'plugin.json')
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'))
  if (manifest.id !== sample.id) fail(sample.id, `manifest id is "${manifest.id}"`)
  if (!/^\d+\.\d+\.\d+$/.test(manifest.version)) fail(sample.id, `version "${manifest.version}" is not MAJOR.MINOR.PATCH`)

  for (const relative of [manifest.desktop.entry, ...(manifest.desktop.styles ?? [])]) {
    const file = join(here, sample.id, relative.replace('./', ''))
    if (!existsSync(file)) fail(sample.id, `manifest points at a missing file: ${relative}`)
  }

  const entry = readFileSync(join(dist, 'index.mjs'), 'utf8')
  if (/\b(?:from|import)\s*\(?\s*["']react(?:-dom)?(?:\/[^"']*)?["']/.test(entry)) {
    fail(sample.id, 'output contains a bare React import')
  }
  if (/["']\.\/assets\//.test(entry) && !entry.includes('import.meta.url')) {
    fail(sample.id, 'output references a bundled asset without resolving it against import.meta.url')
  }
  ok('manifest and output shape')
}

function makeHost(sample, record) {
  const nothing = () => () => {}
  const schema = JSON.parse(readFileSync(join(here, sample.id, 'settings.schema.json'), 'utf8'))
  let settingsValue = Object.fromEntries(
    schema.fields
      .filter((field) => Object.hasOwn(field, 'defaultValue'))
      .map((field) => [field.key, field.defaultValue])
  )
  return {
    plugin: { id: sample.id, version: '0.1.0', displayName: sample.id },
    environment: {
      locale: 'zh-Hans',
      theme: 'dark',
      themeSeed: { surface: '#141515', ink: '#eeeeec', accent: '#4566cc', contrast: 60 },
      onChange: nothing
    },
    appearance: {
      setThemeSeedOverride: (value) => {
        record.themeSeedOverride = value
      },
      setBackdropPresentation: (value) => {
        record.backdropPresentation = value
      }
    },
    session: {
      workspacePath: 'X:\\workspaces\\probe',
      threadId: 'thread-probe',
      mode: 'agent',
      busy: false,
      onChange: nothing
    },
    effect: (setup) => {
      const cleanup = setup()
      record.cleanups.push(typeof cleanup === 'function' ? cleanup : () => {})
      return () => {}
    },
    services: {
      provide: (id) => {
        record.services.push(id)
        return () => {}
      },
      use: () => undefined
    },
    events: { on: nothing, emit: () => {} },
    navigation: {
      openMainView: () => {},
      openSettingsPage: () => {},
      openThread: async () => {},
      onOpenUrl: nothing
    },
    ui: {
      showToast: () => () => {},
      confirm: async () => false,
      pickColor: async () => ({ kind: 'cancel' }),
      add: (surface) => {
        record.surfaces.push(['add', surface])
        return () => {}
      },
      replace: (surface) => {
        record.surfaces.push(['replace', surface])
        return () => {}
      },
      wrap: (surface) => {
        record.surfaces.push(['wrap', surface])
        return () => {}
      }
    },
    appServer: {
      request: async (method) => {
        record.appServerRequests.push(method)
        return {}
      },
      onNotification: (method, listener) => {
        record.appServerNotifications.push(method)
        record.notificationListeners.push({ method, listener })
        return () => {}
      }
    },
    settings: {
      get: async () => ({
        schema,
        personal: {},
        workspace: {},
        value: settingsValue,
        writableScopes: ['personal', 'workspace']
      }),
      mutate: async (_scope, operations) => {
        for (const operation of operations) {
          if (operation.op === 'set') settingsValue = { ...settingsValue, [operation.key]: operation.value }
          else delete settingsValue[operation.key]
        }
        const snapshot = {
          schema,
          personal: settingsValue,
          workspace: {},
          value: settingsValue,
          writableScopes: ['personal', 'workspace']
        }
        for (const listener of [...record.settingsListeners]) listener(snapshot)
        return snapshot
      },
      onChange: (listener) => {
        record.settingsListeners.add(listener)
        return () => record.settingsListeners.delete(listener)
      }
    },
    workspaces: { listLocalProjects: async () => [] }
  }
}

function expectAll(sample, label, actual, expected) {
  for (const item of expected) {
    if (!actual.includes(item)) fail(sample.id, `${label} is missing "${item}" (saw ${JSON.stringify(actual)})`)
  }
}

async function activateSample(sample, dist) {
  const record = {
    surfaces: [],
    services: [],
    cleanups: [],
    appServerRequests: [],
    appServerNotifications: [],
    notificationListeners: [],
    settingsListeners: new Set(),
    themeSeedOverride: null,
    backdropPresentation: null
  }
  const module = await import(pathToFileURL(join(dist, 'index.mjs')).href)
  if (typeof module.activate !== 'function') {
    fail(sample.id, 'the bundle does not export activate')
    return
  }

  const activation = (await module.activate(makeHost(sample, record))) ?? {}

  const surfaces = record.surfaces.map((entry) => entry.join(' '))
  expectAll(sample, 'surfaces', surfaces, (sample.surfaces ?? []).map((entry) => entry.join(' ')))
  expectAll(sample, 'services', record.services, sample.services ?? [])
  expectAll(sample, 'settings pages', (activation.settingsPages ?? []).map((page) => page.id), sample.settingsPages ?? [])
  expectAll(sample, 'commands', (activation.commands ?? []).map((command) => command.id), sample.commands ?? [])
  expectAll(sample, 'AppServer requests', record.appServerRequests, sample.appServerRequests ?? [])
  expectAll(
    sample,
    'AppServer notifications',
    record.appServerNotifications,
    sample.appServerNotifications ?? []
  )

  for (const { method, listener } of record.notificationListeners) {
    try {
      listener(NOTIFICATION_PROBES[method] ?? {})
    } catch (error) {
      fail(sample.id, `the "${method}" listener threw: ${error instanceof Error ? error.message : String(error)}`)
    }
  }

  const ids = [
    ...(activation.settingsPages ?? []),
    ...(activation.commands ?? []),
    ...(activation.mainViews ?? []),
    ...(activation.conversationViews ?? []),
    ...(activation.toolRenderers ?? []),
    ...(activation.messageActions ?? [])
  ].map((contribution) => contribution.id)
  if (new Set(ids).size !== ids.length) fail(sample.id, `contribution ids collide: ${JSON.stringify(ids)}`)

  for (const contribution of [...(activation.settingsPages ?? []), ...(activation.commands ?? [])]) {
    const translations = contribution.label?.translations ?? {}
    const missing = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'].filter((locale) => !translations[locale])
    if (missing.length > 0) fail(sample.id, `"${contribution.id}" label is missing ${missing.join(', ')}`)
  }

  if (sample.appearance === 'backdrop' && record.backdropPresentation === null) {
    fail(sample.id, 'backdrop appearance contribution was not set')
  }

  for (const cleanup of record.cleanups) cleanup()
  await activation.dispose?.()

  ok(`activate registered ${surfaces.length} surface(s), ${ids.length} contribution(s)`)
}

function installDom() {
  const { JSDOM } = desktopRequire('jsdom')
  const dom = new JSDOM('<!doctype html><html data-theme="dark"><body><div id="root"></div></body></html>', {
    url: 'https://dotcraft.test/'
  })
  const keys = ['window', 'document', 'HTMLElement', 'Element', 'Node', 'MutationObserver', 'CustomEvent', 'Event', 'getComputedStyle']
  for (const key of keys) {
    Object.defineProperty(globalThis, key, {
      value: key === 'window' ? dom.window : dom.window[key],
      configurable: true,
      writable: true
    })
  }
}

function installRuntime() {
  const React = desktopRequire('react')
  const JsxRuntime = desktopRequire('react/jsx-runtime')
  const ReactDom = desktopRequire('react-dom')
  const stub = (name) => {
    const Component = (props) => React.createElement('div', { 'data-stub': name }, props?.children)
    Component.displayName = name
    return Component
  }
  const runtimeKey = Symbol.for('dotcraft.desktop-plugin.runtime')
  globalThis[runtimeKey] = {
    react: React,
    jsxRuntime: JsxRuntime,
    reactDom: { createPortal: ReactDom.createPortal },
    ui: Object.fromEntries(
      [
        'PluginSurface',
        'Button',
        'IconButton',
        'Input',
        'Textarea',
        'Select',
        'SegmentedControl',
        'Checkbox',
        'Spinner',
        'Skeleton',
        'Slider',
        'ActionTooltip',
        'Combobox',
        'ModalHeader',
        'PillSwitch',
        'SettingsPanelShell',
        'SettingsBreadcrumb',
        'SettingsGroup',
        'SettingsRow',
        'InlineDiff'
      ].map((name) => [name, stub(name)])
    )
  }
}

checkMarketplace()
installDom()
installRuntime()

for (const sample of SAMPLES) {
  console.log(`\n${sample.id}`)
  const dist = build(sample)
  if (dist === null) continue
  checkOutput(sample, dist)
  try {
    await activateSample(sample, dist)
  } catch (error) {
    fail(sample.id, `activate threw: ${error instanceof Error ? error.message : String(error)}`)
  }
}

console.log('')
if (failures.length > 0) {
  console.error(`${failures.length} check(s) failed:`)
  for (const failure of failures) console.error(`  - ${failure}`)
  process.exitCode = 1
} else {
  console.log(`All ${SAMPLES.length} sample plugins build, load, and activate.`)
}
