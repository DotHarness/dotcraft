import { ICONS } from './icons.js';

export function esc(value) {
  return String(value ?? '').replace(/[&<>"']/g, char => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[char]));
}

export function attrs(map = {}) {
  return Object.entries(map)
    .filter(([, value]) => value !== undefined && value !== null && value !== false)
    .map(([key, value]) => (value === true ? ` ${key}` : ` ${key}="${esc(value)}"`))
    .join('');
}

export function icon(name, size = 16, stroke = 2) {
  return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="${stroke}" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[name]}</svg>`;
}

export function spinner(size = 14) {
  return `<svg class="dc-spinner" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle class="dc-spinner__track" cx="12" cy="12" r="7" stroke="currentColor" stroke-width="2"/><circle cx="12" cy="12" r="7" stroke="currentColor" stroke-width="2" stroke-dasharray="33 44"/></svg>`;
}

export function chevron(expanded) {
  return `<svg class="dc-disclosure-chevron"${expanded ? ' data-expanded' : ''} width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS.chevronRight}</svg>`;
}

export function statusIndicator(tone, label) {
  const body = tone === 'pending'
    ? spinner(14)
    : `<span class="dc-status-indicator__dot"${tone === 'neutral' ? '' : ` data-tone="${tone}"`}></span>`;
  const labelled = label ? ` role="img" aria-label="${esc(label)}"` : ' aria-hidden="true"';
  return `<span class="dc-status-indicator"${labelled}>${body}</span>`;
}

export function statusLabel(tone, label) {
  return `<span class="dash-status">${statusIndicator(tone)}<span>${esc(label)}</span></span>`;
}

export function button(label, { variant = 'secondary', size = 'default', iconName, iconSize, loading, disabled, attributes } = {}) {
  const iconHtml = iconName ? `<span class="dc-button__icon" aria-hidden="true">${icon(iconName, iconSize ?? (size === 'sm' ? 14 : 15))}</span>` : '';
  return `<button type="button" class="dc-button"${attrs({
    'data-variant': variant,
    'data-size': size,
    'data-loading': loading,
    'aria-busy': loading ? 'true' : undefined,
    disabled: disabled || loading,
    ...attributes
  })}>${loading ? `<span class="dc-button__spinner" aria-hidden="true">${spinner(14)}</span>` : ''}${iconHtml}<span class="dc-button__label">${esc(label)}</span></button>`;
}

export function iconButton(iconName, label, { size = 28, iconSize = 15, tone, active, disabled, disabledReason, className, attributes } = {}) {
  const html = `<button type="button" class="dc-icon-button${className ? ` ${className}` : ''}"${attrs({
    'aria-label': label,
    'data-size': size,
    'data-tone': tone,
    'data-active': active,
    disabled,
    ...attributes
  })}>${icon(iconName, iconSize)}</button>`;
  return disabled && disabledReason
    ? `<span class="dc-tooltip-anchor" data-tooltip="${esc(disabledReason)}">${html}</span>`
    : html;
}

export function segmented(ariaLabel, options, value, action, { disabled = false, attributes } = {}) {
  return `<div role="group" class="dash-segmented"${attrs({ 'aria-label': ariaLabel, ...attributes })}>${options.map(option =>
    `<button type="button" class="dash-segmented__option"${attrs({
      'aria-pressed': String(option.value === value),
      'data-action': action,
      'data-value': option.value,
      disabled
    })}>${esc(option.label)}</button>`).join('')}</div>`;
}

export function pillSwitch(checked, label, action) {
  return `<button type="button" role="switch" class="dc-switch"${attrs({ 'aria-checked': String(checked), 'aria-label': label, 'data-action': action })}></button>`;
}

export function panelHeader({ title, titleHtml, description, leading = '', actions = '' }) {
  return `<header class="dash-panel-header">${leading}<div class="dash-panel-header__text">
    <h1 class="dash-panel-title">${titleHtml ?? esc(title)}</h1>
    ${description ? `<p class="dash-panel-description">${description}</p>` : ''}
  </div>${actions ? `<div class="dash-panel-header__actions">${actions}</div>` : ''}</header>`;
}

export function statTile(label, value) {
  return `<div class="dash-stat"><div class="dash-stat__label">${esc(label)}</div><div class="dash-stat__value">${value}</div></div>`;
}

export function statGrid(label, tiles) {
  return `<section class="dash-stat-section"${label ? ` aria-label="${esc(label)}"` : ''}>${label ? `<h2 class="dash-section-label">${esc(label)}</h2>` : ''}<div class="dash-stat-grid">${tiles.join('')}</div></section>`;
}

export function sectionHeading(title) {
  return `<div class="dash-section-heading"><h2>${esc(title)}</h2></div>`;
}

export function emptyState(iconName, title, detail, action = '') {
  return `<div class="dash-empty"><span class="dash-empty__icon" aria-hidden="true">${icon(iconName, 20)}</span>
    <p class="dash-empty__title">${esc(title)}</p>${detail ? `<p class="dash-empty__detail">${esc(detail)}</p>` : ''}${action}</div>`;
}

export function errorNotice(title, detail, action = '') {
  return `<div class="dash-notice dash-notice--error" role="alert">${icon('circleAlert', 20)}
    <div class="dash-notice__text"><div class="dash-notice__title">${esc(title)}</div>${detail ? `<div class="dash-notice__detail">${esc(detail)}</div>` : ''}</div>
    ${action ? `<div class="dash-notice__action">${action}</div>` : ''}</div>`;
}

export function facts(items, wrap = false) {
  return `<span class="${wrap ? 'dash-facts dash-facts--wrap' : 'dash-facts'}">${items.filter(item => item !== '' && item != null && item !== false).map(item => `<span>${item}</span>`).join('')}</span>`;
}

export function kv(items) {
  return `<dl class="dash-kv">${items.map(([key, value]) => `<div><dt>${esc(key)}</dt><dd>${value}</dd></div>`).join('')}</dl>`;
}

export function mono(value) {
  return `<code class="dash-mono">${esc(value ?? '–')}</code>`;
}

export function num(text) {
  return `<span class="dash-num">${esc(text)}</span>`;
}

export function skeleton(width = '100%', height = 12, style = '') {
  const size = value => (typeof value === 'number' ? `${value}px` : value);
  return `<span class="dc-skeleton" aria-hidden="true" style="width:${size(width)};height:${size(height)};${style}"></span>`;
}

function skeletonList(count, lines = ['60%', '35%']) {
  const row = `<div class="dc-skeleton-row">${skeleton(14, 14, 'border-radius:50%')}<div class="dc-skeleton-row__lines">${lines.map(width => skeleton(width, 12)).join('')}</div></div>`;
  return `<div role="status" aria-busy="true" style="display:flex;flex-direction:column;gap:10px">${row.repeat(count)}</div>`;
}

export function loadingPanel() {
  const tiles = Array.from({ length: 5 }, () => `<div class="dash-stat">${skeleton('50%', 11)}${skeleton('70%', 22, 'margin-top:8px')}</div>`).join('');
  return `<div class="dash-panel-header"><div class="dash-panel-header__text">${skeleton(120, 20)}${skeleton(320, 12, 'margin-top:8px')}</div></div>
    <div class="dash-panel-body" aria-busy="true" aria-label="Loading"><div class="dash-stat-grid">${tiles}</div>${skeleton('100%', 10)}${skeletonList(5)}</div>`;
}
