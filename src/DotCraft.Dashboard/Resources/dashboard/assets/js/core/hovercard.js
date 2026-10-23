const OPEN_DELAY_MS = 320;
const CLOSE_DELAY_MS = 120;
const OVERLAP = 8;
const VIEWPORT_PADDING = 8;

const renderers = {};
let card = null;
let anchor = null;
let openTimer = 0;
let closeTimer = 0;

export function registerHoverCard(type, render) {
  renderers[type] = render;
}

function clamp(value, min, max) {
  return max < min ? min : Math.min(Math.max(value, min), max);
}

function open(target) {
  const html = renderers[target.dataset.hovercard]?.(target);
  if (!html) return;
  anchor = target;
  card ??= Object.assign(document.createElement('div'), { className: 'dash-hovercard' });
  card.innerHTML = html;
  document.body.appendChild(card);
  const a = target.getBoundingClientRect();
  const c = card.getBoundingClientRect();
  const rightLeft = a.right - OVERLAP;
  const side = rightLeft + c.width <= innerWidth - VIEWPORT_PADDING ? 'right' : 'left';
  const desired = side === 'right' ? rightLeft : a.left + OVERLAP - c.width;
  const left = clamp(desired, VIEWPORT_PADDING, innerWidth - c.width - VIEWPORT_PADDING);
  card.style.left = `${left}px`;
  card.style.top = `${clamp(a.top - 4, VIEWPORT_PADDING, innerHeight - c.height - VIEWPORT_PADDING)}px`;
  card.dataset.side = side;
  if (Math.abs(left - desired) < 0.5) card.dataset.overlapEdge = side === 'right' ? 'left' : 'right';
  else delete card.dataset.overlapEdge;
}

export function closeHoverCard() {
  clearTimeout(openTimer);
  clearTimeout(closeTimer);
  card?.remove();
  anchor = null;
}

export function initHoverCards() {
  document.addEventListener('mouseover', event => {
    if (card?.contains(event.target)) {
      clearTimeout(closeTimer);
      return;
    }
    const target = event.target.closest?.('[data-hovercard]');
    if (target && target === anchor) {
      clearTimeout(closeTimer);
      return;
    }
    clearTimeout(openTimer);
    if (target) {
      openTimer = setTimeout(() => {
        closeHoverCard();
        open(target);
      }, anchor ? 0 : OPEN_DELAY_MS);
    } else if (anchor) {
      clearTimeout(closeTimer);
      closeTimer = setTimeout(closeHoverCard, CLOSE_DELAY_MS);
    }
  });
  document.addEventListener('mousedown', event => {
    if (!card?.contains(event.target)) closeHoverCard();
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') closeHoverCard();
  });
  document.addEventListener('scroll', closeHoverCard, true);
}
