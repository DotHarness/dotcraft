const GAP = 8;
const VIEWPORT_PADDING = 8;
let tip = null;
let anchor = null;

function show(target) {
  const label = target.dataset.tooltip;
  if (!label) return;
  anchor = target;
  tip ??= Object.assign(document.createElement('div'), { className: 'dc-tooltip', role: 'tooltip' });
  tip.textContent = label;
  document.body.appendChild(tip);
  const a = target.getBoundingClientRect();
  const t = tip.getBoundingClientRect();
  const placement = target.dataset.tooltipPlacement || 'top';
  let left;
  let top;
  if (placement === 'right' && a.right + GAP + t.width <= innerWidth - VIEWPORT_PADDING) {
    left = a.right + GAP;
    top = a.top + (a.height - t.height) / 2;
  } else {
    left = a.left + (a.width - t.width) / 2;
    top = a.top - t.height - GAP >= VIEWPORT_PADDING ? a.top - t.height - GAP : a.bottom + GAP;
  }
  tip.style.left = `${Math.max(VIEWPORT_PADDING, Math.min(left, innerWidth - t.width - VIEWPORT_PADDING))}px`;
  tip.style.top = `${Math.max(VIEWPORT_PADDING, top)}px`;
}

function hideTooltip() {
  tip?.remove();
  anchor = null;
}

export function initTooltips() {
  document.addEventListener('mouseover', event => {
    const target = event.target.closest?.('[data-tooltip]');
    if (target !== anchor) {
      hideTooltip();
      if (target) show(target);
    }
  });
  document.addEventListener('focusin', event => {
    const target = event.target.closest?.('[data-tooltip]');
    if (target && target.matches(':focus-visible')) show(target);
  });
  document.addEventListener('focusout', hideTooltip);
  document.addEventListener('mousedown', hideTooltip);
  document.addEventListener('scroll', hideTooltip, true);
}
