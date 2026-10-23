import assert from 'node:assert/strict';
import { test } from 'node:test';

const elements = {};
const listeners = {};
globalThis.document = {
  getElementById: id => (elements[id] ??= { innerHTML: '', textContent: '', querySelector: () => null, querySelectorAll: () => [] }),
  querySelector: () => null,
  querySelectorAll: () => [],
  addEventListener: (type, listener) => { (listeners[type] ??= []).push(listener); },
};
globalThis.fetch = async () => ({ ok: false, status: 404, json: async () => null });

const assets = '../../src/DotCraft.Dashboard/Resources/dashboard/assets/js/';
const { installActions } = await import(assets + 'core/actions.js');
const { state } = await import(assets + 'core/state.js');
const { renderAutomations } = await import(assets + 'panels/automations.js');
await import(assets + 'panels/sessions.js');
installActions();

const panelHtml = () => elements['panel-automations'].innerHTML;

function clickThreadLink(html) {
  const thread = /data-action="open-thread" data-thread="([^"]*)"/.exec(html)[1];
  const link = { dataset: { action: 'open-thread', thread } };
  listeners.click.forEach(listener => listener({
    target: { closest: selector => (selector === '[data-action]' ? link : null) },
  }));
}

test('unified definitions render names, lifecycle states, schedules and execution targets', () => {
  renderAutomations({
    generatedAt: '2026-09-08T12:00:00Z', countsByStatus: { active: 1, paused: 1, completed: 1 },
    automations: [
      { id: 'a1', name: 'Daily report', status: 'active', executionMode: 'independent', nextRunAt: '2026-09-09T09:00:00Z' },
      { id: 'a2', name: 'Follow PR', status: 'paused', executionMode: 'thread', targetThreadId: 'thread-42' },
      { id: 'a3', name: 'One reminder', status: 'completed', executionMode: 'independent' },
    ],
  });
  for (const text of ['Daily report', 'Follow PR', 'One reminder', 'Active', 'Paused', 'Completed', 'A new run each time', 'Its conversation', 'Next run'])
    assert.ok(panelHtml().includes(text), text);
});

test('conversation link opens its thread in Sessions while independent definitions have no thread link', () => {
  state.panel = 'automations';
  renderAutomations({ automations: [{ id: 'a1', name: 'Follow', status: 'active', executionMode: 'thread', targetThreadId: 'thread-42' }] });
  clickThreadLink(panelHtml());
  assert.equal(state.panel, 'sessions');
  renderAutomations({ automations: [{ id: 'a2', name: 'Standalone', status: 'active', executionMode: 'independent', targetThreadId: 'unused' }] });
  assert.ok(!panelHtml().includes('data-action="open-thread"'));
});

test('user fields are escaped and a missing conversation is not a broken link', () => {
  renderAutomations({ automations: [{ id: 'a1', name: '<img src=x onerror="alert(1)">', status: 'paused', executionMode: 'thread' }] });
  const html = panelHtml();
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
  assert.ok(html.includes('Conversation unavailable'));
  assert.ok(!html.includes('data-action="open-thread"'));
});

test('empty unified snapshot clears rows and shows the empty state', () => {
  renderAutomations({ automations: [{ id: 'a1', name: 'Old row', status: 'active' }] });
  renderAutomations({ automations: [], countsByStatus: {} });
  assert.ok(panelHtml().includes('No automations yet'));
  assert.ok(!panelHtml().includes('Old row'));
});
