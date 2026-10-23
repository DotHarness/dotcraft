import { button, esc, icon, iconButton } from './html.js';

function mount(html, onDismiss) {
  const scrim = document.createElement('div');
  scrim.className = 'dc-dialog-scrim';
  scrim.innerHTML = html;
  const previous = document.activeElement;
  const onKey = event => {
    if (event.key === 'Escape') {
      event.preventDefault();
      onDismiss();
    }
  };
  scrim.addEventListener('mousedown', event => {
    if (event.target === scrim) onDismiss();
  });
  document.addEventListener('keydown', onKey);
  document.body.appendChild(scrim);
  return () => {
    document.removeEventListener('keydown', onKey);
    scrim.remove();
    previous?.focus?.();
  };
}

export function confirmDialog({ title, message, confirmLabel = 'Confirm', danger = false }) {
  return new Promise(resolve => {
    let unmount;
    const finish = result => {
      unmount();
      resolve(result);
    };
    unmount = mount(`<div class="dc-confirm" role="dialog" aria-modal="true" aria-labelledby="dc-confirm-title">
      <h2 class="dc-confirm__title" id="dc-confirm-title">${esc(title)}</h2>
      <p class="dc-confirm__message">${esc(message)}</p>
      <div class="dc-confirm__actions">
        ${button('Cancel', { variant: 'secondary', attributes: { 'data-dialog': 'cancel' } })}
        ${button(confirmLabel, { variant: danger ? 'danger' : 'primary', attributes: { 'data-dialog': 'confirm' } })}
      </div>
    </div>`, () => finish(false));
    const dialog = document.querySelector('.dc-confirm');
    dialog.addEventListener('click', event => {
      const choice = event.target.closest('[data-dialog]')?.dataset.dialog;
      if (choice) finish(choice === 'confirm');
    });
    dialog.querySelector('[data-dialog="cancel"]').focus();
  });
}

export function showTextDialog({ iconName, title, description, text }) {
  let unmount;
  unmount = mount(`<div class="dash-modal" role="dialog" aria-modal="true" aria-labelledby="dash-dialog-title">
    <div class="dc-modal-header">
      <div class="dc-modal-header__top">
        <span class="dc-modal-header__badge">${icon(iconName, 18)}</span>
        ${iconButton('x', 'Close', { size: 30, iconSize: 16, attributes: { 'data-dialog': 'close' } })}
      </div>
      <h2 class="dc-modal-header__title" id="dash-dialog-title">${esc(title)}</h2>
      ${description ? `<p class="dc-modal-header__description">${esc(description)}</p>` : ''}
    </div>
    <pre class="dash-code dash-modal__body">${esc(text)}</pre>
  </div>`, () => unmount());
  const close = document.querySelector('.dash-modal [data-dialog="close"]');
  close.addEventListener('click', () => unmount());
  close.focus();
}
