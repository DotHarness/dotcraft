import { onAction } from './actions.js';
import { attrs, esc, icon } from './html.js';

const VIEWPORT_INSET = 8;
let open = null;

function close() {
  if (!open) return;
  open.menu.remove();
  open.anchor.setAttribute('aria-expanded', 'false');
  document.removeEventListener('mousedown', onOutside, true);
  document.removeEventListener('keydown', onKey, true);
  window.removeEventListener('resize', close);
  document.removeEventListener('scroll', onScroll, true);
  const { anchor, restoreFocus } = open;
  open = null;
  if (restoreFocus) anchor.focus();
}

function onOutside(event) {
  if (!open.menu.contains(event.target) && !open.anchor.contains(event.target)) close();
}

function onScroll(event) {
  if (!open.menu.contains(event.target)) close();
}

function onKey(event) {
  const items = [...open.menu.querySelectorAll('[data-menu-index]:not(:disabled)')];
  const index = items.indexOf(document.activeElement);
  if (event.key === 'Escape') {
    event.preventDefault();
    open.restoreFocus = true;
    close();
  } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    const step = event.key === 'ArrowDown' ? 1 : -1;
    items[(index + step + items.length) % items.length]?.focus();
  } else if (event.key === 'Tab') {
    close();
  }
}

function place(menu, anchor, align, minWidth) {
  const rect = anchor.getBoundingClientRect();
  const width = Math.max(minWidth, menu.offsetWidth);
  const height = menu.offsetHeight;
  const left = align === 'right' ? rect.right - width : rect.left;
  const below = rect.bottom + (align === 'select' ? 6 : 4);
  const top = below + height > innerHeight - VIEWPORT_INSET ? rect.top - height - 6 : below;
  menu.style.left = `${Math.max(VIEWPORT_INSET, Math.min(left, innerWidth - width - VIEWPORT_INSET))}px`;
  menu.style.top = `${Math.max(VIEWPORT_INSET, top)}px`;
}

function mount(anchor, menu, onPick, align, minWidth) {
  close();
  document.body.appendChild(menu);
  place(menu, anchor, align, minWidth);
  anchor.setAttribute('aria-expanded', 'true');
  open = { anchor, menu, restoreFocus: false };
  menu.addEventListener('click', event => {
    const item = event.target.closest('[data-menu-index]');
    if (!item || item.disabled) return;
    open.restoreFocus = true;
    close();
    onPick(Number(item.dataset.menuIndex));
  });
  document.addEventListener('mousedown', onOutside, true);
  document.addEventListener('keydown', onKey, true);
  window.addEventListener('resize', close);
  document.addEventListener('scroll', onScroll, true);
  (menu.querySelector('[aria-selected="true"]') ?? menu.querySelector('[data-menu-index]:not(:disabled)'))?.focus({ preventScroll: true });
}

export function openMenu(anchor, items, { align = 'left' } = {}) {
  const menu = document.createElement('div');
  menu.className = 'dc-menu';
  menu.setAttribute('role', 'menu');
  menu.innerHTML = items.map((item, index) => item === 'separator'
    ? '<div class="dc-menu__separator" role="separator"></div>'
    : `<button type="button" class="dc-menu__item"${attrs({
      role: 'menuitem',
      'data-menu-index': index,
      'data-danger': item.danger,
      disabled: item.disabled,
      'data-tooltip': item.disabled ? item.title : undefined,
      'data-tooltip-placement': 'right'
    })}>${item.iconName ? `<span class="dc-menu__icon">${icon(item.iconName, 14)}</span>` : ''}${esc(item.label)}</button>`).join('');
  mount(anchor, menu, index => items[index].onSelect(), align, 200);
}

export function selectHtml({ id, value, options, ariaLabel, appearance = 'field', change, width }) {
  const current = options.find(option => option.value === value) ?? options[0];
  return `<button type="button" class="dc-settings-select" role="combobox"${attrs({
    id,
    value: current?.value ?? '',
    'aria-label': ariaLabel,
    'aria-haspopup': 'listbox',
    'aria-expanded': 'false',
    'data-appearance': appearance,
    'data-action': 'open-select',
    'data-change': change,
    'data-options': JSON.stringify(options),
    style: width ? `min-width:${width}px` : undefined
  })}><span class="dc-settings-select__value">${esc(current?.label ?? '')}</span><span class="dc-settings-select__chevron">${icon('chevronDown', 15, 1.8)}</span></button>`;
}

function openSelect(trigger) {
  if (open?.anchor === trigger) {
    close();
    return;
  }
  const options = JSON.parse(trigger.dataset.options || '[]');
  const menu = document.createElement('div');
  menu.className = 'dc-settings-select-menu';
  menu.setAttribute('role', 'listbox');
  menu.setAttribute('aria-label', trigger.getAttribute('aria-label') || '');
  const width = Math.min(Math.max(trigger.offsetWidth, 160), innerWidth - VIEWPORT_INSET * 2);
  menu.style.width = `${width}px`;
  menu.style.maxHeight = '280px';
  menu.innerHTML = options.map((option, index) => {
    const selected = option.value === trigger.value;
    return `<button type="button" class="dc-settings-select-option"${attrs({
      role: 'option',
      'aria-selected': String(selected),
      'data-menu-index': index,
      'data-active': selected
    })}><span class="dc-settings-select-option__label">${esc(option.label)}</span><span class="dc-settings-select-option__check" aria-hidden="true">${selected ? icon('check', 15, 1.8) : ''}</span></button>`;
  }).join('');
  menu.addEventListener('mouseover', event => {
    const item = event.target.closest('[data-menu-index]');
    if (!item) return;
    menu.querySelectorAll('[data-active]').forEach(el => el.removeAttribute('data-active'));
    item.setAttribute('data-active', '');
  });
  mount(trigger, menu, index => {
    const option = options[index];
    if (option.value === trigger.value) return;
    trigger.value = option.value;
    trigger.querySelector('.dc-settings-select__value').textContent = option.label;
    trigger.dispatchEvent(new Event('change', { bubbles: true }));
  }, 'select', width);
  const selected = menu.querySelector('[aria-selected="true"]');
  if (selected) menu.scrollTop = selected.offsetTop - (menu.clientHeight - selected.offsetHeight) / 2;
}

onAction('click', { 'open-select': el => openSelect(el) });
