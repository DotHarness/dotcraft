const DASH = '–';

export function formatCount(n) {
  if (n == null || Number.isNaN(n)) return DASH;
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 10_000) return `${(n / 1000).toFixed(1)}K`;
  return Number(n).toLocaleString('en-US');
}

export function formatPercent(value, digits = 1) {
  if (value == null || Number.isNaN(value)) return DASH;
  return `${(value * 100).toFixed(digits)}%`;
}

export function formatDuration(ms) {
  if (ms == null || Number.isNaN(ms)) return DASH;
  if (ms < 1000) return `${Math.round(ms)} ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)} s`;
  return `${Math.floor(ms / 60_000)}m ${Math.round((ms % 60_000) / 1000)}s`;
}

export function formatClock(ts) {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return DASH;
  const pad = (v, n = 2) => String(v).padStart(n, '0');
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`;
}

export function formatRelative(ts) {
  const time = Date.parse(ts || '');
  if (Number.isNaN(time)) return DASH;
  const diff = Date.now() - time;
  if (diff < 0) return formatUntil(ts);
  if (diff < 60_000) return 'just now';
  if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}m ago`;
  if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)}h ago`;
  return `${Math.floor(diff / 86_400_000)}d ago`;
}

export function formatUntil(ts) {
  const time = Date.parse(ts || '');
  if (Number.isNaN(time)) return 'Not scheduled';
  const diff = time - Date.now();
  if (diff <= 0) return 'Due now';
  const minutes = Math.round(diff / 60_000);
  if (minutes < 60) return `in ${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `in ${hours}h${minutes % 60 ? ` ${minutes % 60}m` : ''}`;
  return `in ${Math.floor(hours / 24)}d${hours % 24 ? ` ${hours % 24}h` : ''}`;
}

export function formatDateTime(ts) {
  const d = new Date(ts || '');
  if (Number.isNaN(d.getTime())) return DASH;
  const month = d.toLocaleString('en-US', { month: 'short' });
  const pad = v => String(v).padStart(2, '0');
  return `${month} ${d.getDate()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function humanizeKey(key) {
  return String(key || '')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .replace(/([a-z\d])([A-Z])/g, '$1 $2')
    .replace(/\bApi\b/g, 'API')
    .replace(/\bUrl\b/g, 'URL')
    .replace(/\bId\b/g, 'ID')
    .replace(/\bMcp\b/g, 'MCP')
    .trim();
}

export function shortHash(value) {
  return value ? String(value).replace(/^sha256:/, '').slice(0, 12) : DASH;
}

export function parseMetadata(json) {
  if (!json) return {};
  try {
    const value = JSON.parse(json);
    return value && typeof value === 'object' ? value : {};
  } catch {
    return {};
  }
}

export function prettyJson(raw) {
  if (raw == null || raw === '') return '';
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return String(raw);
  }
}

// Shows only what the user typed, without harness-injected <system-reminder> blocks.
function cleanUserRequest(text) {
  return String(text || '')
    .replace(/<system-reminder>[\s\S]*?<\/system-reminder>/gi, ' ')
    .replace(/<system-reminder>[\s\S]*$/i, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

export function sessionTitle(session) {
  return cleanUserRequest(session?.firstUserRequest) || session?.sessionKey || '';
}

export function plural(count, one, many = `${one}s`) {
  return `${formatCount(count)} ${count === 1 ? one : many}`;
}
