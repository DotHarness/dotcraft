import { onAction } from '../core/actions.js';
import { fetchJson } from '../core/api.js';
import { showTextDialog } from '../core/dialog.js';
import { copyButton } from '../core/clipboard.js';
import { formatCount, formatPercent, formatRelative, plural, sessionTitle } from '../core/format.js';
import { esc, icon, iconButton } from '../core/html.js';
import { registerHoverCard } from '../core/hovercard.js';
import { state } from '../core/state.js';
import {
  abnormalFinish, cacheVerdict, firstRequestText, forkModeText, inheritsParentContext, prefixText, rollbackText, troubleMarker
} from './session-cache.js';

export async function loadSessions() {
  const sessions = await fetchJson('/sessions');
  state.sessions = Array.isArray(sessions) ? sessions : [];
  return state.sessions;
}

export function findSession(key) {
  return state.sessions.find(session => session.sessionKey === key) ?? null;
}

function cardRow(iconName, html, className = '') {
  return `<div class="dash-hovercard__row${className ? ` ${className}` : ''}">${icon(iconName, 14)}<span>${html}</span></div>`;
}

function sessionCard(target) {
  const session = findSession(target.dataset.thread);
  if (!session) return '';
  const parent = session.parentSessionKey ? findSession(session.parentSessionKey) : null;
  const children = state.sessions.filter(candidate => candidate.parentSessionKey === session.sessionKey).length;
  const tools = session.toolNames ?? [];
  const rows = [cardRow('database', cacheVerdict(session))];
  if (session.rollbackCount > 0) rows.push(cardRow('undo2', rollbackText(session)));
  if (session.parentSessionKey) {
    rows.push(cardRow('gitBranch', `Started by <button type="button" class="dash-link" data-action="open-thread" data-thread="${esc(session.parentSessionKey)}">${esc(parent ? sessionTitle(parent) : session.parentSessionKey)}</button>`, 'dash-hovercard__row--single'));
    rows.push(cardRow('layers', forkModeText(session)));
    if (firstRequestText(session)) rows.push(cardRow('gauge', firstRequestText(session)));
    if (inheritsParentContext(session) && session.parentPrefix) rows.push(cardRow('link2', prefixText(session.parentPrefix)));
  }
  if (children) rows.push(cardRow('gitFork', esc(plural(children, 'subagent'))));
  if (abnormalFinish(session)) rows.push(cardRow('circleAlert', esc(abnormalFinish(session))));
  rows.push(cardRow('wrench', tools.length ? `<span data-tooltip="${esc(tools.join(', '))}">${esc(plural(tools.length, 'tool'))}</span>` : 'No tools recorded'));
  const systemPrompt = session.finalSystemPrompt
    ? iconButton('fileText', 'System prompt', { size: 24, iconSize: 13, attributes: { 'data-tooltip': 'System prompt', 'data-action': 'show-system-prompt', 'data-thread': session.sessionKey } })
    : '';
  return `<div class="dash-hovercard__header">
      <span class="dash-hovercard__title">${esc(sessionTitle(session))}</span>
      <span class="dash-hovercard__meta">${formatRelative(session.lastActivityAt)}</span>
      <span class="dash-hovercard__actions">${copyButton(session.sessionKey, 'Copy thread ID')}${systemPrompt}</span>
    </div>
    ${rows.join('')}`;
}

registerHoverCard('session', sessionCard);

function row(session, canDelete) {
  const title = sessionTitle(session);
  const deleteLabel = session.deletionScope === 'threadCascade' ? 'Delete conversation and traces' : 'Delete traces';
  return `<div class="dash-table__row dash-session" role="row" data-thread="${esc(session.sessionKey)}">
    <span role="cell" class="dash-session__main">
      <button type="button" class="dash-session__title" data-action="open-thread" data-thread="${esc(session.sessionKey)}" data-hovercard="session"><span class="dash-session__name">${esc(title)}</span></button>
      ${troubleMarker(session)}
    </span>
    <span role="cell" class="dash-col-num dash-num">${formatCount(session.totalTokens)}</span>
    <span role="cell" class="dash-col-num dash-num dash-col--opt2">${session.totalTokens ? formatPercent(session.cacheHitRate, 0) : '–'}</span>
    <span role="cell" class="dash-col-num dash-num dash-dim dash-col--opt1">${formatRelative(session.lastActivityAt)}</span>
    <span role="cell" class="dash-row-actions">${canDelete ? iconButton('trash2', deleteLabel, { tone: 'danger', attributes: { 'data-action': 'delete-session', 'data-thread': session.sessionKey } }) : ''}</span>
  </div>`;
}

function allThreadsRow(key) {
  return `<div class="dash-table__row dash-session" role="row">
    <span role="cell" class="dash-session__main"><button type="button" class="dash-session__title dash-session__title--all" data-action="open-thread" data-thread="${key}">${icon('rows3', 16)}<span class="dash-session__name">All threads</span></button></span>
  </div>`;
}

export function sessionTable(sessions, { listId, canDelete = false, allThreadsKey = null }) {
  return `<div class="dash-table dash-sessions" role="table" aria-label="Threads" data-list="${listId}">
    <div class="dash-table__head" role="row">
      <span role="columnheader">Thread</span>
      <span role="columnheader" class="dash-col-num">Tokens</span>
      <span role="columnheader" class="dash-col-num dash-col--opt2">Cached</span>
      <span role="columnheader" class="dash-col-num dash-col--opt1">Last active</span>
      <span aria-hidden="true"></span>
    </div>
    ${allThreadsKey ? allThreadsRow(allThreadsKey) : ''}
    ${sessions.map(session => row(session, canDelete)).join('')}
  </div>`;
}

onAction('click', {
  'show-system-prompt': el => {
    const session = findSession(el.dataset.thread);
    showTextDialog({ iconName: 'fileText', title: 'System prompt', description: session ? sessionTitle(session) : '', text: session?.finalSystemPrompt || 'Not captured yet.' });
  }
});
