import { icons } from 'lucide-react'

const CDN_SOURCES = [
  'https://cdnjs.cloudflare.com', 'https://cdn.jsdelivr.net', 'https://esm.sh',
  'https://fonts.bunny.net', 'https://fonts.googleapis.com',
  'https://fonts.gstatic.com', 'https://unpkg.com'
].join(' ')

export const INLINE_VISUALIZATION_MIN_HEIGHT = 120
export const INLINE_VISUALIZATION_MAX_HEIGHT = 10_000

export interface InlineVisualizationThemeTokens {
  background: string
  foreground: string
  card: string
  cardForeground: string
  primary: string
  primaryForeground: string
  secondary: string
  secondaryForeground: string
  muted: string
  mutedForeground: string
  accent: string
  accentForeground: string
  border: string
  input: string
  ring: string
  fontFamily: string
}

const BASE_CSS = String.raw`
:root{color-scheme:light dark;--background:light-dark(rgb(255 255 255),rgb(24 24 24));--foreground:light-dark(rgb(26 28 31),rgb(255 255 255));--card:color-mix(in srgb,var(--foreground) 5%,transparent);--card-foreground:var(--foreground);--primary:light-dark(rgb(51 156 255),rgb(131 195 255));--primary-foreground:var(--background);--secondary:light-dark(rgb(248 248 248),rgb(38 38 38));--secondary-foreground:var(--foreground);--muted:color-mix(in srgb,var(--foreground) 10%,transparent);--muted-foreground:color-mix(in srgb,var(--foreground) 52%,transparent);--accent:var(--muted);--accent-foreground:var(--foreground);--destructive:light-dark(rgb(226 85 7),rgb(255 133 73));--border:color-mix(in srgb,var(--foreground) 12%,transparent);--input:var(--border);--ring:var(--primary);--font-size-base:13px;--viz-container-width:100%;--viz-series-1:var(--primary);--viz-series-2:light-dark(rgb(243 136 59),rgb(245 154 86));--viz-series-3:light-dark(rgb(93 201 119),rgb(116 213 139));--viz-series-4:light-dark(rgb(235 119 177),rgb(240 143 192));--viz-series-5:light-dark(rgb(155 121 236),rgb(170 145 239));--viz-series-6:light-dark(rgb(58 185 177),rgb(90 203 194));font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;font-size:max(11px,var(--font-size-base));line-height:1.45;color:var(--foreground);background:transparent}
:root[data-theme=light]{color-scheme:light}:root[data-theme=dark]{color-scheme:dark}*{box-sizing:border-box}html,body{margin:0;padding:0;background:transparent}body{overflow:hidden}.viz-root{min-width:0;margin:0;padding:0;border:0;border-radius:0;background:transparent}body>.card:only-child{padding:0;border:0;border-radius:0;background:transparent}img,canvas,svg,video{display:block;max-width:100%;height:auto}button,input,select,textarea{font:inherit}.card{min-width:0;padding:12px;overflow:hidden;border:1px solid var(--border);border-radius:8px;color:var(--card-foreground);background:var(--card)}.viz-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(180px,100%),1fr));gap:10px}.viz-row,.viz-controls{display:flex;flex-wrap:wrap;align-items:center;gap:8px}.viz-stat{display:flex;min-width:0;flex-direction:column;gap:2px}.viz-stat-value{font-size:1.4em;font-weight:600;line-height:1.2}.viz-badge{display:inline-flex;align-items:center;padding:3px 8px;border-radius:999px;color:var(--accent-foreground);background:var(--accent)}.text-small{font-size:max(11px,calc(var(--font-size-base) - 2px))}.text-muted{color:var(--muted-foreground)}.text-center{text-align:center!important}.text-end{text-align:end!important}.sr-only{position:absolute;width:1px;height:1px;padding:0;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}.btn,button{display:inline-flex;min-height:32px;align-items:center;justify-content:center;gap:6px;padding:0 12px;border:1px solid var(--border);border-radius:8px;color:var(--secondary-foreground);background:var(--secondary);cursor:pointer}.btn:hover,button:hover{border-color:var(--input);background:var(--muted)}.btn:active,button:active{background:var(--accent)}.btn-primary{border-color:var(--foreground);color:var(--background);background:var(--foreground);font-weight:600}.btn-primary:hover,.btn-primary:active{border-color:var(--foreground);color:var(--background);background:var(--foreground);opacity:.9}.btn-ghost{border-color:transparent;color:var(--muted-foreground);background:transparent}.btn-ghost:hover,.btn-ghost:active{border-color:transparent;color:var(--foreground);background:var(--muted)}.btn.active,.btn[aria-pressed=true],.btn[data-state=active],button.active,button[aria-pressed=true],button[data-state=active]{border-color:var(--foreground);color:var(--background);background:var(--foreground)}.btn:disabled,button:disabled{cursor:not-allowed;opacity:.55}.btn:focus-visible,button:focus-visible,.form-control:focus-visible,.form-select:focus-visible,.form-range:focus-visible,.form-check:focus-visible{outline:2px solid var(--ring);outline-offset:2px}.form-label{display:block;margin-block-end:4px;color:var(--foreground)}.form-control,.form-select{display:block;width:100%;min-height:32px;padding:0 8px;border:1px solid var(--input);border-radius:8px;color:var(--foreground);background:var(--secondary)}textarea.form-control{padding-block:6px;resize:vertical}.form-range{display:block;width:100%;height:32px;accent-color:var(--primary)}.form-check{width:16px;height:16px;margin:0;accent-color:var(--primary)}.form-switch{appearance:none;width:32px;height:18px;padding:2px;border:1px solid var(--input);border-radius:999px;background:var(--muted);cursor:pointer}.form-switch:before{display:block;width:12px;height:12px;border-radius:50%;background:var(--foreground);content:"";transition:transform .15s ease}.form-switch:checked{background:var(--primary)}.form-switch:checked:before{transform:translateX(14px);background:var(--primary-foreground)}.table-responsive{width:100%;overflow-x:auto}.table{width:100%;border-collapse:collapse;color:var(--foreground)}.table th,.table td{padding:8px 16px 8px 0;border-bottom:1px solid var(--border);text-align:start;vertical-align:top}.table th{font-weight:600}[data-tooltip]{position:relative}[data-tooltip]:hover:after,[data-tooltip]:focus-visible:after{position:absolute;z-index:20;inset-block-end:calc(100% + 6px);inset-inline-start:50%;max-width:min(260px,80vw);padding:5px 8px;border:1px solid var(--border);border-radius:6px;color:var(--foreground);background:var(--secondary);box-shadow:0 4px 16px rgb(0 0 0 / 18%);content:attr(data-tooltip);font-size:11px;line-height:1.3;white-space:normal;transform:translateX(-50%);pointer-events:none}
@media(max-width:736px){.card{padding:10px}.viz-grid{grid-template-columns:repeat(auto-fit,minmax(min(150px,100%),1fr))}.table th,.table td{padding:7px 10px 7px 0}}
@media(max-width:320px){.viz-grid{grid-template-columns:1fr}.viz-controls{align-items:stretch;flex-direction:column}.viz-controls>*{width:100%}}
@media(prefers-reduced-motion:reduce){*,*:before,*:after{scroll-behavior:auto!important;transition-duration:.01ms!important;animation-duration:.01ms!important;animation-iteration-count:1!important}}
:root[data-reduced-motion=true] *, :root[data-reduced-motion=true] *:before, :root[data-reduced-motion=true] *:after{scroll-behavior:auto!important;transition-duration:.01ms!important;animation-duration:.01ms!important;animation-iteration-count:1!important}
`

export function buildInlineVisualizationDocument(
  fragment: string,
  theme: 'light' | 'dark',
  locale: string,
  viewId: string,
  themeTokens?: Partial<InlineVisualizationThemeTokens>
): string {
  const csp = [
    "default-src 'none'",
    `script-src 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' ${CDN_SOURCES}`,
    `style-src 'unsafe-inline' ${CDN_SOURCES}`,
    `img-src data: blob: ${CDN_SOURCES}`,
    `font-src data: ${CDN_SOURCES}`,
    `media-src data: blob: ${CDN_SOURCES}`,
    'worker-src blob:', "connect-src 'none'", "frame-src 'none'", "object-src 'none'",
    "base-uri 'none'", "form-action 'none'", "navigate-to 'none'"
  ].join('; ')
  const iconNodes = collectLucideIconNodes()
  const bridge = String.raw`(() => {
    const pending = new Map(); let sequence = 0; let resizeTimer = 0;
    const viewId = ${JSON.stringify(viewId)};
    const iconNodes = ${JSON.stringify(iconNodes)};
    const tokenNames = ${JSON.stringify(THEME_TOKEN_CSS_VARIABLES)};
    const applyTokens = value => { if (!value || typeof value !== 'object') return; for (const [name, cssName] of Object.entries(tokenNames)) { const token = value[name]; if (typeof token === 'string' && token) document.documentElement.style.setProperty(cssName, token); } };
    applyTokens(${JSON.stringify(themeTokens ?? {})});
    const send = (method, params) => parent.postMessage({ method, params: { ...params, viewId } }, '*');
    const openai = Object.freeze({ sendFollowUpMessage(options) {
      const prompt = typeof options?.prompt === 'string' ? options.prompt : '';
      const title = typeof options?.title === 'string' ? options.title.slice(0, 250) : undefined;
      const id = 'followup-' + (++sequence);
      return new Promise((resolve, reject) => { pending.set(id, { resolve, reject }); send('visualization/followUp', { id, prompt, title }); });
    }});
    Object.defineProperty(window, 'openai', { value: openai, configurable: false, writable: false });
    const createIcons = (options = {}) => {
      document.querySelectorAll('[data-lucide]').forEach(placeholder => {
        const name = (placeholder.getAttribute('data-lucide') || '').toLowerCase();
        const node = iconNodes[name]; if (!node) return;
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        for (const attribute of placeholder.attributes) if (attribute.name !== 'data-lucide') svg.setAttribute(attribute.name, attribute.value);
        const attrs = { xmlns:'http://www.w3.org/2000/svg', width:'16', height:'16', viewBox:'0 0 24 24', fill:'none', stroke:'currentColor', 'stroke-width':'2', 'stroke-linecap':'round', 'stroke-linejoin':'round', ...(options.attrs || {}) };
        for (const [key, value] of Object.entries(attrs)) if (!svg.hasAttribute(key)) svg.setAttribute(key, String(value));
        svg.classList.add('lucide', 'lucide-' + name);
        for (const [tag, attributes] of node) { const child = document.createElementNS('http://www.w3.org/2000/svg', tag); for (const [key, value] of Object.entries(attributes)) child.setAttribute(key, String(value)); svg.appendChild(child); }
        placeholder.replaceWith(svg);
      });
    };
    window.lucide = Object.freeze({ createIcons });
    addEventListener('message', event => {
      if (event.source !== parent) return;
      const message = event.data;
      if (message?.params?.viewId !== viewId) return;
      if (message?.method === 'visualization/theme') { document.documentElement.dataset.theme = message.params?.theme === 'light' ? 'light' : 'dark'; applyTokens(message.params?.tokens); }
      if (message?.method === 'visualization/context') {
        const value = message.params || {};
        if (typeof value.locale === 'string') document.documentElement.lang = value.locale;
        if (typeof value.timeZone === 'string') document.documentElement.dataset.timeZone = value.timeZone;
        document.documentElement.dataset.reducedMotion = value.reducedMotion ? 'true' : 'false';
        document.documentElement.dataset.pointer = value.pointer === 'coarse' ? 'coarse' : 'fine';
        if (typeof value.width === 'number') document.documentElement.style.setProperty('--viz-container-width', value.width + 'px');
        applyTokens(value.tokens);
      }
      if (message?.method === 'visualization/overflow') document.body.style.overflow = message.params?.enabled ? 'auto' : 'hidden';
      if (message?.method === 'visualization/followUpResult') { const entry = pending.get(message.params?.id); if (!entry) return; pending.delete(message.params.id); message.params.ok ? entry.resolve(message.params.result) : entry.reject(new Error(message.params.error || 'Cancelled')); }
    });
    const report = () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(() => send('visualization/resize', { height: Math.max(document.documentElement.scrollHeight, document.body.scrollHeight) }), 100); };
    addEventListener('DOMContentLoaded', () => {
      createIcons({ attrs: { width: 16, height: 16 } });
      new ResizeObserver(report).observe(document.documentElement);
      document.fonts?.ready?.then(report); report(); send('visualization/ready', {});
    }, { once: true });
  })();`
  return `<!doctype html><html lang="${escapeAttribute(locale)}" data-theme="${theme}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="referrer" content="no-referrer"><meta http-equiv="Content-Security-Policy" content="${escapeAttribute(csp)}"><style>${BASE_CSS}</style></head><body><script>${bridge}</script>${fragment}</body></html>`
}

const THEME_TOKEN_CSS_VARIABLES: Record<keyof InlineVisualizationThemeTokens, string> = {
  background: '--background', foreground: '--foreground', card: '--card',
  cardForeground: '--card-foreground', primary: '--primary', primaryForeground: '--primary-foreground',
  secondary: '--secondary', secondaryForeground: '--secondary-foreground', muted: '--muted',
  mutedForeground: '--muted-foreground', accent: '--accent', accentForeground: '--accent-foreground',
  border: '--border', input: '--input', ring: '--ring', fontFamily: 'font-family'
}

type LucideNode = Array<[string, Record<string, string>]>

function collectLucideIconNodes(): Record<string, LucideNode> {
  const result: Record<string, LucideNode> = {}
  for (const [exportName, component] of Object.entries(
    icons as unknown as Record<string, { render?: (props: object) => { props?: { iconNode?: LucideNode } } }>
  )) {
    const node = component?.render?.({})?.props?.iconNode
    if (!node) continue
    const name = exportName
      .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
      .replace(/([A-Z])([A-Z][a-z])/g, '$1-$2')
      .toLowerCase()
    result[name] = node
  }
  return result
}

function escapeAttribute(value: string): string {
  return value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;')
}
