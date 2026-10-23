import { formatCount, formatPercent } from '../core/format.js';
import { esc } from '../core/html.js';

const SERIES = ['var(--chart-series-1)', 'var(--chart-series-2)', 'var(--chart-series-3)', 'var(--chart-series-4)'];

export function tokenMixBar(title, segments) {
  const total = segments.reduce((sum, segment) => sum + (segment.value || 0), 0);
  const bar = total > 0
    ? segments.map((segment, index) => segment.value > 0
      ? `<span style="display:flex;flex:${segment.value} 1 0;min-width:3px" data-tooltip="${esc(`${segment.label}: ${formatCount(segment.value)}, ${formatPercent(segment.value / total)}`)}"><span class="dash-mix__segment" style="background:${SERIES[index]}"></span></span>`
      : '').join('')
    : '<span class="dash-mix__segment dash-mix__segment--empty"></span>';
  return `<figure class="dash-mix" aria-label="${esc(title)}">
    <figcaption class="dash-mix__caption"><span>${esc(title)}</span><span class="dash-num">${formatCount(total)} tokens</span></figcaption>
    <div class="dash-mix__bar" role="img" aria-label="${esc(segments.map(segment => `${segment.label} ${formatCount(segment.value)}`).join(', '))}">${bar}</div>
    <ul class="dash-legend">${segments.map((segment, index) => `<li>
      <span class="dash-legend__swatch" style="background:${SERIES[index]}" aria-hidden="true"></span>
      <span class="dash-legend__label">${esc(segment.label)}</span>
      <span class="dash-legend__value dash-num">${formatCount(segment.value)}</span>
      <span class="dash-legend__share dash-num">${total ? formatPercent(segment.value / total, 0) : '–'}</span>
    </li>`).join('')}</ul>
  </figure>`;
}

export function horizontalBars(title, data) {
  const max = Math.max(1, ...data.map(datum => datum.value));
  return `<figure class="dash-bars" aria-label="${esc(title)}">
    <figcaption class="dash-mix__caption"><span>${esc(title)}</span></figcaption>
    <ul>${data.map(datum => `<li class="dash-bars__row">
      <span class="dash-bars__label">${esc(datum.label)}</span>
      <span class="dash-bars__track"><span style="display:flex;width:${(datum.value / max) * 100}%;min-width:3px" data-tooltip="${esc(`${datum.label}: ${formatCount(datum.value)} tokens, ${datum.detail}`)}"><span class="dash-bars__fill"></span></span></span>
      <span class="dash-bars__value dash-num">${formatCount(datum.value)}</span>
    </li>`).join('')}</ul>
  </figure>`;
}
