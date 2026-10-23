import { formatDuration } from '../core/format.js';
import { esc } from '../core/html.js';
import { describeEvent } from './trace-describe.js';
import { entryLabel } from './trace-ledger.js';

const LANES = [
  { id: 'input', label: 'Input', types: ['Request', 'AgentInstructions', 'WorldStateDiagnostic'] },
  { id: 'model', label: 'Model', types: [] },
  { id: 'tools', label: 'Tools', types: ['DeferredToolLoading', 'ToolInjection', 'ToolPolicyWithheld'] }
];
const ERROR_TYPES = ['Error', 'ProviderError'];

function laneOf(entry) {
  if (entry.kind === 'tool') return 'tools';
  return LANES.find(lane => lane.types.includes(entry.event.type))?.id ?? 'model';
}

function span(entry, previousEnd) {
  const start = Date.parse(entry.time);
  if (entry.kind === 'tool') {
    const end = entry.completed ? Date.parse(entry.completed.timestamp) : start;
    return [start, end];
  }
  if (entry.event.type === 'TokenUsage' && previousEnd != null && previousEnd < start) return [previousEnd, start];
  return [start, start];
}

function marks(entries) {
  const result = [];
  let previousEnd = null;
  for (const entry of entries) {
    if (entry.kind === 'turn') continue;
    const lane = laneOf(entry);
    const [start, end] = span(entry, previousEnd);
    if (!Number.isFinite(start)) continue;
    const event = entry.kind === 'tool' ? entry.completed ?? entry.started : entry.event;
    const description = describeEvent(event);
    result.push({ entry, lane, start, end, error: ERROR_TYPES.includes(event.type) || description.status === 'error', label: entryLabel(entry) });
    if (lane !== 'model') previousEnd = Math.max(previousEnd ?? end, end);
  }
  return result;
}

export function traceStrip(entries, selectedKey, range) {
  const all = marks(entries);
  if (all.length < 2) return '';
  const first = Math.min(...all.map(mark => mark.start));
  const last = Math.max(...all.map(mark => mark.end));
  const total = Math.max(last - first, 1);
  const pct = value => ((value - first) / total) * 100;
  const outside = mark => range && (mark.end < range[0] || mark.start > range[1]);
  const selection = range
    ? `<div class="dash-strip__range" style="--from:${(pct(range[0]) / 100).toFixed(4)};--to:${(pct(range[1]) / 100).toFixed(4)}"></div>`
    : '';
  return `<div class="dash-strip" aria-label="Timeline" data-first="${first}" data-last="${last}"${range ? ' data-ranged' : ''}>
    ${selection}
    ${LANES.map(lane => `<div class="dash-strip__lane" data-lane="${lane.id}">
      <span class="dash-strip__label">${lane.label}</span>
      <div class="dash-strip__track">${all.filter(mark => mark.lane === lane.id).map(mark => {
        const width = pct(mark.end) - pct(mark.start);
        const tip = mark.end > mark.start ? `${mark.label} ${formatDuration(mark.end - mark.start)}` : mark.label;
        return `<button type="button" class="dash-strip__mark" data-action="select-trace-entry" data-key="${esc(mark.entry.key)}" data-tooltip="${esc(tip)}"${mark.error ? ' data-error' : ''}${outside(mark) ? ' data-out' : ''}${mark.entry.key === selectedKey ? ' aria-current="true"' : ''} style="left:${pct(mark.start).toFixed(3)}%;width:${width.toFixed(3)}%"></button>`;
      }).join('')}</div>
    </div>`).join('')}
  </div>`;
}
