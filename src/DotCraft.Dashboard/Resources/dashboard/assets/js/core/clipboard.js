import { onAction } from './actions.js';
import { iconButton } from './html.js';
import { showToast } from './toast.js';

export function copyButton(value, label) {
  return iconButton('copy', label, { size: 24, iconSize: 13, attributes: { 'data-action': 'copy-value', 'data-value': value } });
}

onAction('click', {
  'copy-value': async el => {
    try {
      await navigator.clipboard.writeText(el.dataset.value);
      showToast('Copied.', 'success', 1600);
    } catch {
      showToast('Couldn’t copy to the clipboard.', 'error');
    }
  }
});
