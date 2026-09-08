import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { createContext, runInContext } from 'node:vm';

const source = readFileSync(new URL('../../src/DotCraft.Dashboard/Resources/DashBoardAutomations.js', import.meta.url), 'utf8');

function page() {
  const elements = Object.fromEntries(['automationsStats', 'automationsGeneratedAt', 'automationsTable']
    .map(id => [id, { innerHTML: '', textContent: '', onclick: null }]));
  const opened = [];
  const context = createContext({
    document: { getElementById: id => elements[id] },
    openSession: id => opened.push(id),
    _automationsLastData: null,
  });
  runInContext(source, context);
  return { context, elements, opened };
}

test('unified definitions render names, lifecycle states, schedules and execution targets', () => {
  const { context, elements } = page();
  context.renderAutomations({
    generatedAt: '2026-09-08T12:00:00Z', countsByStatus: { active: 1, paused: 1, completed: 1 },
    automations: [
      { id: 'a1', name: 'Daily report', status: 'active', executionMode: 'independent', nextRunAt: '2026-09-09T09:00:00Z' },
      { id: 'a2', name: 'Follow PR', status: 'paused', executionMode: 'thread', targetThreadId: 'thread-42' },
      { id: 'a3', name: 'One reminder', status: 'completed', executionMode: 'independent' },
    ],
  });
  const html = elements.automationsTable.innerHTML;
  for (const text of ['Daily report', 'Follow PR', 'One reminder', 'Active', 'Paused', 'Completed', 'Independent run', 'Continue conversation', 'Next run'])
    assert.ok(html.includes(text), text);
  assert.ok(html.includes('data-automation-thread="thread-42"'));
  assert.ok(elements.automationsStats.innerHTML.includes('Active'));
  assert.ok(elements.automationsGeneratedAt.textContent.startsWith('Data as of '));
});

test('conversation link opens its target while independent definitions have no thread link', () => {
  const { context, elements, opened } = page();
  context.renderAutomations({ automations: [{ id: 'a1', name: 'Follow', status: 'active', executionMode: 'thread', targetThreadId: 'thread-42' }] });
  let prevented = false;
  elements.automationsTable.onclick({
    target: { closest: () => ({ dataset: { automationThread: 'thread-42' } }) },
    preventDefault: () => { prevented = true; },
  });
  assert.deepEqual(opened, ['thread-42']);
  assert.equal(prevented, true);
  context.renderAutomations({ automations: [{ id: 'a2', name: 'Standalone', status: 'active', executionMode: 'independent', targetThreadId: 'unused' }] });
  assert.ok(!elements.automationsTable.innerHTML.includes('data-automation-thread'));
});

test('user fields are escaped and a missing conversation is not a broken link', () => {
  const { context, elements } = page();
  context.renderAutomations({ automations: [{ id: 'a1', name: '<img src=x onerror="alert(1)">', status: 'paused', executionMode: 'thread' }] });
  const html = elements.automationsTable.innerHTML;
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
  assert.ok(html.includes('Conversation unavailable'));
  assert.ok(!html.includes('data-automation-thread'));
});

test('empty unified snapshot clears rows and displays zero', () => {
  const { context, elements } = page();
  context.renderAutomations({ automations: [{ id: 'a1', name: 'Old row', status: 'active' }] });
  context.renderAutomations({ automations: [], countsByStatus: {} });
  assert.ok(elements.automationsTable.innerHTML.includes('No automations.'));
  assert.ok(!elements.automationsTable.innerHTML.includes('Old row'));
  assert.ok(elements.automationsStats.innerHTML.includes('>0<'));
  assert.equal(elements.automationsGeneratedAt.textContent, '');
});
