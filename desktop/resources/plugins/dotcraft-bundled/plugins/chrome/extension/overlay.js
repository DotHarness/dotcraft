(() => {
  if (globalThis.__dotcraftAutomationOverlay) return;
  globalThis.__dotcraftAutomationOverlay = true;

  const GLOW = '#339cff';
  const CURSOR_PATH = 'M4.7 3.9 21.8 14l-8.2 1.7L9.5 23.3 4.7 3.9Z';
  const IDLE_X = 0.58;
  const IDLE_Y = 0.55;
  const MIN_GLIDE_MS = 180;
  const MAX_GLIDE_MS = 520;

  const host = document.createElement('dotcraft-automation-overlay');
  host.setAttribute('data-dotcraft-automation-overlay', '');
  host.setAttribute('aria-hidden', 'true');
  host.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483646;pointer-events:none;display:none;';
  const root = host.attachShadow({ mode: 'closed' });
  root.innerHTML = `
    <style>
      :host { all: initial; }
      @media print { :host { display: none !important; } }
      .edge {
        position: fixed;
        inset: 0;
        pointer-events: none;
        box-shadow: inset 0 0 0 2px rgba(51, 156, 255, 0.55), inset 0 0 28px 6px rgba(51, 156, 255, 0.28);
        animation: pulse 2.4s ease-in-out infinite;
      }
      .cursor {
        position: fixed;
        top: 0;
        left: 0;
        width: 28px;
        height: 28px;
        margin: -3.9px 0 0 -4.7px;
        pointer-events: none;
        transition-property: transform;
        transition-timing-function: cubic-bezier(0.2, 0.8, 0.2, 1);
        will-change: transform;
        filter: drop-shadow(0 0 6px color-mix(in srgb, ${GLOW} 90%, transparent))
          drop-shadow(0 0 15px color-mix(in srgb, ${GLOW} 48%, transparent))
          drop-shadow(0 3px 7px rgba(0, 0, 0, 0.35));
      }
      .cursor path { fill: #2f8af5; stroke: #fff; stroke-width: 2; stroke-linejoin: round; }
      @keyframes pulse { 0%, 100% { opacity: 0.75; } 50% { opacity: 1; } }
      @media (prefers-reduced-motion: reduce) { .edge { animation: none; } .cursor { transition: none !important; } }
    </style>
    <div class="edge"></div>
    <svg class="cursor" viewBox="0 0 28 28"><path d="${CURSOR_PATH}"></path></svg>
  `;
  const cursor = root.querySelector('.cursor');

  let active = false;
  let suppressed = false;
  let position = null;
  let pendingMove = null;

  function attach() {
    if (!host.isConnected) (document.documentElement || document).appendChild(host);
  }

  function render() {
    if (!active) {
      host.remove();
      return;
    }
    attach();
    host.style.display = suppressed ? 'none' : 'block';
  }

  function placeCursor(point, durationMs) {
    cursor.style.transitionDuration = `${durationMs}ms`;
    cursor.style.transform = `translate3d(${point.x}px, ${point.y}px, 0)`;
    position = point;
  }

  function idlePoint() {
    return { x: Math.round(window.innerWidth * IDLE_X), y: Math.round(window.innerHeight * IDLE_Y) };
  }

  function nextFrame() {
    return new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  }

  function settlePending() {
    const current = pendingMove;
    pendingMove = null;
    if (!current) return;
    clearTimeout(current.timer);
    current.resolve({ ok: true, sequence: current.sequence });
  }

  function move(message) {
    settlePending();
    const target = { x: Number(message.x), y: Number(message.y) };
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const from = position ?? idlePoint();
    const distance = Math.hypot(target.x - from.x, target.y - from.y);
    if (!message.animate || !active || suppressed || reduced || distance < 0.5) {
      placeCursor(target, 0);
      return Promise.resolve({ ok: true, sequence: message.sequence });
    }
    if (!position) placeCursor(from, 0);
    const duration = Math.round(Math.min(MAX_GLIDE_MS, Math.max(MIN_GLIDE_MS, distance * 0.6)));
    return new Promise((resolve) => {
      const finish = () => settlePending();
      pendingMove = { sequence: message.sequence, resolve, timer: setTimeout(finish, duration + 80) };
      cursor.addEventListener('transitionend', finish, { once: true });
      requestAnimationFrame(() => placeCursor(target, duration));
    });
  }

  async function setState(message) {
    active = message.active === true;
    suppressed = message.suppressed === true;
    if (active) {
      if (!position) placeCursor(idlePoint(), 0);
    } else {
      settlePending();
    }
    render();
    if (suppressed || !active) await nextFrame();
    return { ok: true };
  }

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message?.type !== 'dotcraft-overlay') return false;
    const task = message.action === 'move' ? move(message) : setState(message);
    task.then(sendResponse, () => sendResponse({ ok: false }));
    return true;
  });
})();
