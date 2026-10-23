import { onAction } from './actions.js';
import { esc, icon, iconButton } from './html.js';

const LEAVE_MS = 220;
const ICONS = { success: 'circleCheck', warning: 'triangleAlert', error: 'circleX', info: 'info' };

function stack() {
  let el = document.querySelector('.dc-toast-stack');
  if (!el) {
    el = document.createElement('div');
    el.className = 'dc-toast-stack';
    el.setAttribute('aria-live', 'polite');
    document.body.appendChild(el);
  }
  return el;
}

function dismiss(toast) {
  if (toast.hasAttribute('data-leaving')) return;
  toast.setAttribute('data-leaving', '');
  setTimeout(() => toast.remove(), LEAVE_MS);
}

export function showToast(message, type = 'info', duration = 5000) {
  const toast = document.createElement('div');
  toast.className = 'dc-toast__surface';
  if (type !== 'info') toast.dataset.tone = type;
  toast.setAttribute('data-entering', '');
  toast.setAttribute('role', type === 'error' ? 'alert' : 'status');
  toast.innerHTML = `<span class="dc-toast__icon" aria-hidden="true">${icon(ICONS[type] || 'info', 16, 2.2)}</span>
    <p class="dc-toast__title">${esc(message)}</p>
    ${iconButton('x', 'Close', { size: 24, iconSize: 14, className: 'dc-toast__close', attributes: { 'data-action': 'dismiss-toast' } })}`;
  stack().prepend(toast);
  requestAnimationFrame(() => requestAnimationFrame(() => toast.removeAttribute('data-entering')));
  if (duration > 0) setTimeout(() => dismiss(toast), duration);
}

onAction('click', { 'dismiss-toast': el => dismiss(el.closest('.dc-toast__surface')) });
