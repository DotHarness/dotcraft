function sendRuntimeMessage(message) {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage(message, (response) => {
      const error = chrome.runtime.lastError;
      resolve(error ? { ok: false, error: error.message } : response);
    });
  });
}

function safePopupError(error) {
  if (!error) return null;
  return String(error)
    .replace(/\\\\\.\\pipe\\dotcraft-chrome-[^\s"'<>]+/g, '[Chrome backend pipe]')
    .replace(/(?:^|\s)\/[^\s"'<>]*dotcraft-chrome-[^\s"'<>]+\.sock/g, ' [Chrome backend socket]')
    .trim();
}

export function statusViewModel(status, fallbackError = null) {
  const state = fallbackError ? 'disconnected' : status?.state ?? 'disconnected';
  const error = safePopupError(fallbackError || status?.error || null);
  const version = status?.version ?? chrome.runtime.getManifest().version;
  if (state === 'connected') {
    return {
      label: 'Connected',
      className: 'is-connected',
      message: 'Chrome backend ready. Control Chrome with DotCraft.',
      version
    };
  }
  if (state === 'reconnecting') {
    return {
      label: 'Reconnecting',
      className: 'is-reconnecting',
      message: error
        ? `Reconnecting to DotCraft automatically. ${error}`
        : 'Reconnecting to DotCraft automatically.',
      version
    };
  }
  return {
    label: 'Disconnected',
    className: 'is-disconnected',
    message: error
      ? `Chrome backend disconnected. ${error}`
      : 'Chrome backend disconnected. Open DotCraft, then refresh status in DotCraft settings.',
    version
  };
}

export function renderStatus(root, status, fallbackError = null) {
  const view = statusViewModel(status, fallbackError);
  const pill = root.querySelector('[data-status-pill]');
  const label = root.querySelector('[data-status-label]');
  const message = root.querySelector('[data-status-message]');
  const version = root.querySelector('[data-version]');

  pill?.classList.remove('is-loading', 'is-connected', 'is-reconnecting', 'is-disconnected');
  pill?.classList.add(view.className);
  if (label) label.textContent = view.label;
  if (message) message.textContent = view.message;
  if (version) version.textContent = `Version ${view.version}`;
}

export async function refreshStatus(root) {
  const response = await sendRuntimeMessage({ type: 'dotcraft-popup-status' });
  if (response?.ok === true) {
    renderStatus(root, response.status);
    return response.status;
  }
  renderStatus(root, null, response?.error || 'Unable to contact the extension worker.');
  return null;
}

export async function openSettings(root) {
  const response = await sendRuntimeMessage({ type: 'dotcraft-popup-open-settings' });
  if (response?.status) {
    renderStatus(root, response.status, response.ok ? null : response.error);
  } else if (response?.ok !== true) {
    renderStatus(root, null, response?.error || 'Unable to open DotCraft settings.');
  }
}

if (typeof document !== 'undefined') {
  document.addEventListener('DOMContentLoaded', () => {
    const root = document;
    const settingsButton = root.querySelector('[data-settings-button]');
    settingsButton?.addEventListener('click', () => {
      void openSettings(root);
    });

    void refreshStatus(root).then((status) => {
      if (status?.state !== 'connected') {
        setTimeout(() => {
          void refreshStatus(root);
        }, 400);
      }
    });
  });
}
