import assert from 'node:assert/strict';
import { test } from 'node:test';
import { installActions } from '../../src/DotCraft.Dashboard/Resources/dashboard/assets/js/core/actions.js';
import { fetchJson, requestJson } from '../../src/DotCraft.Dashboard/Resources/dashboard/assets/js/core/api.js';
import { connectLiveEvents } from '../../src/DotCraft.Dashboard/Resources/dashboard/assets/js/core/connection.js';
import { initWorkspacePicker } from '../../src/DotCraft.Dashboard/Resources/dashboard/assets/js/core/workspace-picker.js';

const alpha = 'a'.repeat(32);
const beta = 'b'.repeat(32);
const stopped = 'c'.repeat(32);
const disabled = 'd'.repeat(32);
const scope = id => `/dashboard/workspaces/${id}/`;

function browser(pathname, hash = '') {
  const assigned = [];
  const replaced = [];
  globalThis.location = {
    pathname, hash,
    assign: path => assigned.push(path),
    replace: path => replaced.push(path)
  };
  return { assigned, replaced };
}

function picker(shared = true) {
  const listeners = {};
  const actions = {};
  const label = {};
  let opened;
  let resolveRendered;
  const trigger = {
    value: '',
    offsetWidth: 160,
    get dataset() { return { action: /data-action="([^"]*)"/.exec(slot.innerHTML)[1] }; },
    addEventListener: (name, listener) => { listeners[name] = listener; },
    getBoundingClientRect: () => ({ top: 0, left: 0, right: 160, bottom: 48 }),
    getAttribute: () => 'Workspace',
    setAttribute: () => {},
    toggleAttribute: () => {},
    querySelector: () => label
  };
  const slot = { hidden: true, innerHTML: '', querySelector: () => trigger };
  const elements = { workspaceSelector: slot, workspaceLabel: {}, workspaceNotice: { hidden: true } };
  globalThis.document = {
    body: { hasAttribute: () => shared, appendChild: menu => { opened = menu; } },
    getElementById: id => elements[id],
    addEventListener: (name, listener) => { actions[name] = listener; },
    removeEventListener: () => {},
    createElement: () => ({
      style: {},
      offsetWidth: 240,
      offsetHeight: 80,
      setAttribute: () => {},
      addEventListener: () => {},
      querySelector: () => null,
      remove: () => {},
      set innerHTML(value) {
        if (this.markup !== undefined) resolveRendered();
        this.markup = value;
      }
    })
  };
  globalThis.window = { addEventListener: () => {}, removeEventListener: () => {} };
  globalThis.innerWidth = 800;
  globalThis.innerHeight = 600;
  installActions();

  return {
    choose: async id => {
      trigger.value = id;
      await listeners.change();
    },
    open: async () => {
      const rendered = new Promise(resolve => { resolveRendered = resolve; });
      actions.click({ target: { closest: () => trigger } });
      await rendered;
      return [...opened.markup.matchAll(/<span class="dc-settings-select-option__label">([^<]*)<\/span>/g)]
        .map(match => match[1]);
    },
    close: () => { actions.click({ target: { closest: () => trigger } }); },
    selectedLabel: () => label.textContent
  };
}

test('shared landing waits for a selection and lists only available workspaces', async () => {
  const { assigned } = browser('/dashboard/');
  const { open, close, choose } = picker();
  globalThis.fetch = async url => {
    assert.equal(url, '/dashboard/api/workspaces');
    return Response.json({ workspaces: [
      { id: alpha, name: 'Alpha', state: 'available' },
      { id: stopped, name: 'Stopped', state: 'stopped' },
      { id: disabled, name: 'Disabled', state: 'disabled' }
    ] });
  };

  assert.equal(await initWorkspacePicker(() => false), false);
  assert.deepEqual(assigned, []);
  assert.deepEqual(await open(), ['Alpha']);
  close();
  await choose(alpha);
  assert.deepEqual(assigned, [scope(alpha)]);
});

test('a stale workspace link keeps the selector usable without falling back silently', async () => {
  const { assigned } = browser(scope(alpha), '#dreams/run/run-1');
  const { choose } = picker();
  globalThis.fetch = async () => Response.json({ workspaces: [{ id: beta, name: 'Beta', state: 'available' }] });

  assert.equal(await initWorkspacePicker(() => false), false);
  assert.deepEqual(assigned, []);
  await choose(beta);
  assert.deepEqual(assigned, [scope(beta)]);
});

test('opening the workspace selector refreshes available options without leaving unsaved settings', async () => {
  const { assigned, replaced } = browser(scope(alpha));
  const { open, close, selectedLabel } = picker();
  let reads = 0;
  globalThis.fetch = async () => Response.json({ workspaces: ++reads < 3 ? [
    { id: alpha, name: 'Project', state: 'available' },
    { id: stopped, name: 'Other', state: 'available' }
  ] : [
    { id: alpha, name: 'Project (first)', state: 'available' },
    { id: beta, name: 'Project (second)', state: 'available' },
    { id: stopped, name: 'Other', state: 'stopped' }
  ] });
  let selectionAttempts = 0;
  assert.equal(await initWorkspacePicker(() => { selectionAttempts++; return true; }), true);

  assert.deepEqual(await open(), ['Project', 'Other']);
  close();
  assert.deepEqual(await open(), ['Project (first)', 'Project (second)']);
  assert.equal(selectedLabel(), 'Project (first)');
  close();

  assert.equal(reads, 3);
  assert.equal(selectionAttempts, 0);
  assert.deepEqual(assigned, []);
  assert.deepEqual(replaced, []);
});

test('workspace requests and sign-in stay scoped, while standalone uses its existing API', async () => {
  const { replaced } = browser(scope(alpha), '#dreams/run/run-1');
  const urls = [];
  globalThis.fetch = async (url, options) => {
    urls.push([url, options?.method ?? 'GET']);
    if (url.endsWith('/config/models')) {
      return Response.json({ errorCode: 'Unauthorized', errorMessage: 'Check your API key' }, { status: 401 });
    }
    if (url.endsWith('/runtime')) return Response.json({ error: 'unauthorized' }, { status: 401 });
    return Response.json({});
  };

  await requestJson('/config/workspace', 'POST', { name: 'Alpha' });
  await assert.rejects(requestJson('/config/models', 'GET'), /Check your API key/);
  assert.deepEqual(replaced, []);
  await fetchJson('/runtime');
  assert.deepEqual(replaced, [scope(alpha) + 'login#dreams/run/run-1']);

  browser('/dashboard/');
  picker(false);
  assert.equal(await initWorkspacePicker(() => false), true);
  await fetchJson('/summary');
  assert.deepEqual(urls, [
    [scope(alpha) + 'api/config/workspace', 'POST'],
    [scope(alpha) + 'api/config/models', 'GET'],
    [scope(alpha) + 'api/runtime', 'GET'],
    ['/dashboard/api/summary', 'GET']
  ]);
});

test('the selected workspace opens its own event stream and navigation closes it', () => {
  browser(scope(beta));
  const listeners = {};
  const streams = [];
  globalThis.window = { addEventListener: (name, listener) => { listeners[name] = listener; } };
  globalThis.document = { getElementById: () => ({}) };
  globalThis.EventSource = class {
    constructor(url) {
      this.url = url;
      this.closed = false;
      streams.push(this);
    }
    close() { this.closed = true; }
  };
  let reconnected = false;
  connectLiveEvents(() => {}, () => { reconnected = true; });
  assert.equal(streams[0].url, scope(beta) + 'api/events/stream');
  listeners.pagehide();
  assert.equal(streams[0].closed, true);

  listeners.pageshow({ persisted: true });
  assert.equal(streams[1].url, scope(beta) + 'api/events/stream');
  streams[1].onopen();
  assert.equal(reconnected, true);
  listeners.pagehide();
});
