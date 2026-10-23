import { onAction } from './actions.js';
import { API } from './api.js';
import { button, errorNotice } from './html.js';

const RETRY_MS = 3000;
let source = null;
let retryTimer = null;
let lost = false;
let handlers = null;

function renderNotice(reconnecting) {
  const slot = document.getElementById('connectionNotice');
  slot.hidden = !lost;
  if (!lost) {
    slot.innerHTML = '';
    return;
  }
  slot.innerHTML = errorNotice('Lost connection to DotCraft',
    'Live updates are paused and the numbers below may be out of date. The Dashboard keeps trying to reconnect.',
    button('Retry now', { size: 'sm', loading: reconnecting, attributes: { 'data-action': 'retry-connection' } })
  );
}

function connect() {
  clearTimeout(retryTimer);
  source?.close();
  source = new EventSource(API + '/events/stream');
  source.onopen = () => {
    if (!lost) return;
    lost = false;
    renderNotice(false);
    handlers.onReconnect();
  };
  source.onmessage = event => {
    try {
      handlers.onEvent(JSON.parse(event.data));
    } catch (error) {
      console.error('Dashboard live event failed:', error);
    }
  };
  source.onerror = () => {
    source.close();
    lost = true;
    renderNotice(false);
    retryTimer = setTimeout(() => {
      renderNotice(true);
      connect();
    }, RETRY_MS);
  };
}

export function connectLiveEvents(onEvent, onReconnect) {
  handlers = { onEvent, onReconnect };
  connect();
}

onAction('click', {
  'retry-connection': () => {
    renderNotice(true);
    connect();
  }
});
