import type { JsonValue } from '@dotcraft/sdk/contracts'
import { icons } from 'lucide-react'
import { VISUALIZATION_DOWNLOAD_METHOD, VISUALIZATION_MAX_DOWNLOAD_BYTES } from '../../../shared/mcpAppSandbox'
import baseStyles from './visualizationRuntime/base.css?raw'
import carouselStyles from './visualizationRuntime/carousel.css?raw'
import controlStyles from './visualizationRuntime/controls.css?raw'
import formStyles from './visualizationRuntime/forms.css?raw'
import layoutStyles from './visualizationRuntime/layout.css?raw'
import installCalendar from './visualizationRuntime/calendar.js?raw'
import installCarousel from './visualizationRuntime/carousel.js?raw'
import installColorInputs from './visualizationRuntime/colors.js?raw'
import installHostActions from './visualizationRuntime/hostActions.js?raw'
import installKeyRelay from './visualizationRuntime/keys.js?raw'
import installRangeProgress from './visualizationRuntime/ranges.js?raw'
import installSelectMenus from './visualizationRuntime/selects.js?raw'
import installTabs from './visualizationRuntime/tabs.js?raw'
import installTooltips from './visualizationRuntime/tooltips.js?raw'
import installTweak from './visualizationRuntime/tweak.js?raw'

const CDN_SOURCES = [
  'https://cdnjs.cloudflare.com', 'https://cdn.jsdelivr.net', 'https://esm.sh',
  'https://fonts.bunny.net', 'https://fonts.googleapis.com',
  'https://fonts.gstatic.com', 'https://unpkg.com'
].join(' ')

export const VISUALIZATION_MIN_HEIGHT = 120
export const VISUALIZATION_MAX_HEIGHT = 10_000

export interface VisualizationThemeTokens {
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
  popover: string
  popoverForeground: string
  destructive: string
  primaryHover: string
  onPrimary: string
  dimmedForeground: string
  borderSubtle: string
  elevated: string
  warning: string
  shadowSm: string
  shadowMd: string
  tooltipShadow: string
  tooltipRadius: string
  controlHeight: string
  controlHeightSm: string
  controlRadius: string
  controlTextRadius: string
  controlPaddingX: string
  fontSizeControl: string
  lineHeightControl: string
  fontSizeSmall: string
  lineHeightSmall: string
  fontMono: string
  scrollbarSize: string
  scrollbarThumbInset: string
  scrollbarThumb: string
  scrollbarThumbHover: string
  scrollbarThumbActive: string
  blue: string
  orange: string
  green: string
  red: string
  purple: string
  yellow: string
  fontFamily: string
  fontSizeBase: string
  cursorInteraction: string
}

const RUNTIME_STYLES = [baseStyles, controlStyles, formStyles, layoutStyles, carouselStyles].join('\n')

export interface VisualizationWidgetState {
  modelContent: JsonValue | null
  privateContent: JsonValue | null
}

export type VisualizationDisplayMode = 'inline' | 'fullscreen'

export interface VisualizationRuntimeLabels {
  design: string
  designs: string
  previousDesign: string
  nextDesign: string
}

export interface VisualizationGlobals {
  widgetState: VisualizationWidgetState | null
  displayMode: VisualizationDisplayMode
  maxHeight: number
}

export function buildVisualizationDocument(
  fragment: string,
  theme: 'light' | 'dark',
  locale: string,
  viewId: string,
  globals: VisualizationGlobals,
  labels: VisualizationRuntimeLabels,
  themeTokens?: Partial<VisualizationThemeTokens>
): string {
  const csp = [
    "default-src 'none'",
    `script-src 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' ${CDN_SOURCES}`,
    `style-src 'unsafe-inline' ${CDN_SOURCES}`,
    `img-src data: blob: ${CDN_SOURCES}`,
    `font-src data: ${CDN_SOURCES}`,
    `media-src data: blob: ${CDN_SOURCES}`,
    'worker-src blob:', "connect-src 'none'", "frame-src 'none'", "object-src 'none'",
    "base-uri 'none'", "form-action 'none'"
  ].join('; ')
  const iconNodes = collectLucideIconNodes()
  const bridge = String.raw`(() => {
    const pending = new Map(); const handlers = new Map(); let sequence = 0; let resizeTimer = 0;
    const viewId = ${JSON.stringify(viewId)};
    const iconNodes = ${JSON.stringify(iconNodes)};
    const tokenNames = ${JSON.stringify(THEME_TOKEN_CSS_VARIABLES)};
    const applyTokens = value => { if (!value || typeof value !== 'object') return; for (const [name, cssName] of Object.entries(tokenNames)) { const token = value[name]; if (typeof token === 'string' && token) document.documentElement.style.setProperty(cssName, token); } };
    applyTokens(${scriptJson(themeTokens ?? {})});
    const send = (method, params) => parent.postMessage({ method, params: { ...params, viewId } }, '*');
    const request = (method, params) => new Promise((resolve, reject) => {
      const id = 'request-' + (++sequence);
      pending.set(id, { resolve, reject });
      try { send(method, { ...params, id }); } catch (error) { pending.delete(id); reject(error); }
    });
    const toJson = value => { const text = JSON.stringify(value === undefined ? null : value); return text === undefined ? null : JSON.parse(text); };
    let widgetState = ${scriptJson(globals.widgetState)};
    let displayMode = ${JSON.stringify(globals.displayMode)};
    let maxHeight = ${JSON.stringify(globals.maxHeight)};
    const openai = Object.freeze({
      get widgetState() { return widgetState; },
      get displayMode() { return displayMode; },
      get maxHeight() { return maxHeight; },
      setWidgetState(state) {
        let snapshot;
        try { snapshot = { modelContent: toJson(state?.modelContent), privateContent: toJson(state?.privateContent) }; } catch (error) { return Promise.reject(error); }
        widgetState = snapshot;
        return request('visualization/setWidgetState', { state: snapshot }).then(() => undefined);
      },
      sendFollowUpMessage(options) {
        const prompt = typeof options?.prompt === 'string' ? options.prompt : '';
        const title = typeof options?.title === 'string' ? options.title.slice(0, 250) : undefined;
        let context;
        try { context = options?.context == null ? undefined : toJson(options.context); } catch (error) { return Promise.reject(error); }
        return request('visualization/followUp', { prompt, title, context });
      },
      requestDisplayMode(options) {
        const mode = options?.mode;
        if (mode !== 'inline' && mode !== 'fullscreen') return Promise.reject(new TypeError('Unsupported display mode'));
        return request('visualization/requestDisplayMode', { mode }).then(result => ({ mode: result?.mode === 'fullscreen' ? 'fullscreen' : 'inline' }));
      }
    });
    Object.defineProperty(window, 'openai', { value: openai, configurable: false, writable: false });
    const buildIcon = (name, attrs, source) => {
      const node = iconNodes[name]; if (!node) return null;
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      if (source) for (const attribute of source.attributes) if (attribute.name !== 'data-lucide') svg.setAttribute(attribute.name, attribute.value);
      const defaults = { xmlns:'http://www.w3.org/2000/svg', width:'16', height:'16', viewBox:'0 0 24 24', fill:'none', stroke:'currentColor', 'stroke-width':'2', 'stroke-linecap':'round', 'stroke-linejoin':'round', ...(attrs || {}) };
      for (const [key, value] of Object.entries(defaults)) if (!svg.hasAttribute(key)) svg.setAttribute(key, String(value));
      svg.classList.add('lucide', 'lucide-' + name);
      for (const [tag, attributes] of node) { const child = document.createElementNS('http://www.w3.org/2000/svg', tag); for (const [key, value] of Object.entries(attributes)) child.setAttribute(key, String(value)); svg.appendChild(child); }
      return svg;
    };
    const createIcons = (options = {}) => {
      document.querySelectorAll('[data-lucide]').forEach(placeholder => {
        const svg = buildIcon((placeholder.getAttribute('data-lucide') || '').toLowerCase(), options.attrs, placeholder);
        if (svg) placeholder.replaceWith(svg);
      });
    };
    window.lucide = Object.freeze({ createIcons });
    const host = Object.freeze({
      send,
      request,
      on: (method, handler) => handlers.set(method, handler),
      labels: Object.freeze(${scriptJson(labels)}),
      icon: (name, size) => { const svg = buildIcon(name, { width: size, height: size }); svg?.setAttribute('aria-hidden', 'true'); return svg; },
      displayMode: () => displayMode,
      downloadMethod: ${JSON.stringify(VISUALIZATION_DOWNLOAD_METHOD)},
      maxDownloadBytes: ${VISUALIZATION_MAX_DOWNLOAD_BYTES}
    });
    for (const install of [${installHostActions.trim()}, ${installTooltips.trim()}, ${installTabs.trim()}, ${installCalendar.trim()}, ${installCarousel.trim()}, ${installTweak.trim()}, ${installKeyRelay.trim()}, ${installRangeProgress.trim()}, ${installColorInputs.trim()}, ${installSelectMenus.trim()}]) install(host);
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
      if (message?.method === 'visualization/overflow') document.documentElement.dataset.overflow = message.params?.enabled ? 'true' : 'false';
      if (message?.method === 'visualization/globals') {
        const globals = message.params?.globals || {};
        if ('widgetState' in globals) widgetState = globals.widgetState ?? null;
        if ('displayMode' in globals) displayMode = globals.displayMode === 'fullscreen' ? 'fullscreen' : 'inline';
        if (typeof globals.maxHeight === 'number') maxHeight = globals.maxHeight;
        dispatchEvent(new CustomEvent('openai:set_globals', { detail: { globals } }));
      }
      handlers.get(message?.method)?.(message.params);
      if (message?.method === 'visualization/result') { const entry = pending.get(message.params?.id); if (!entry) return; pending.delete(message.params.id); message.params.ok ? entry.resolve(message.params.result) : entry.reject(new Error(message.params.error || 'Cancelled')); }
    });
    const report = () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(() => send('visualization/resize', { height: Math.ceil(document.body.getBoundingClientRect().height) }), 100); };
    addEventListener('DOMContentLoaded', () => {
      createIcons({ attrs: { width: 16, height: 16 } });
      new ResizeObserver(report).observe(document.body);
      document.fonts?.ready?.then(report); report(); send('visualization/ready', {});
    }, { once: true });
  })();`
  return `<!doctype html><html lang="${escapeAttribute(locale)}" data-theme="${theme}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="referrer" content="no-referrer"><meta http-equiv="Content-Security-Policy" content="${escapeAttribute(csp)}"><style>${RUNTIME_STYLES}</style></head><body><script>${bridge}</script>${fragment}</body></html>`
}

const THEME_TOKEN_CSS_VARIABLES: Record<keyof VisualizationThemeTokens, string> = {
  background: '--background', foreground: '--foreground', card: '--card',
  cardForeground: '--card-foreground', primary: '--primary', primaryForeground: '--primary-foreground',
  secondary: '--secondary', secondaryForeground: '--secondary-foreground', muted: '--muted',
  mutedForeground: '--muted-foreground', accent: '--accent', accentForeground: '--accent-foreground',
  border: '--border', input: '--input', ring: '--ring', popover: '--popover',
  popoverForeground: '--popover-foreground', destructive: '--destructive', primaryHover: '--primary-hover',
  onPrimary: '--on-primary', dimmedForeground: '--dimmed-foreground', borderSubtle: '--border-subtle',
  elevated: '--elevated', warning: '--warning', shadowSm: '--shadow-sm', shadowMd: '--shadow-md',
  tooltipShadow: '--tooltip-shadow', tooltipRadius: '--tooltip-radius', controlHeight: '--control-height',
  controlHeightSm: '--control-height-sm', controlRadius: '--control-radius', controlTextRadius: '--control-text-radius',
  controlPaddingX: '--control-padding-x', fontSizeControl: '--font-size-control', lineHeightControl: '--line-height-control',
  fontSizeSmall: '--font-size-small', lineHeightSmall: '--line-height-small', fontMono: '--font-mono', scrollbarSize: '--scrollbar-size',
  scrollbarThumbInset: '--scrollbar-thumb-inset', scrollbarThumb: '--scrollbar-thumb', scrollbarThumbHover: '--scrollbar-thumb-hover',
  scrollbarThumbActive: '--scrollbar-thumb-active', blue: '--blue',
  orange: '--orange', green: '--green', red: '--red', purple: '--purple', yellow: '--yellow',
  fontFamily: 'font-family', fontSizeBase: '--font-size-base', cursorInteraction: '--cursor-interaction'
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

function scriptJson(value: unknown): string {
  return JSON.stringify(value).replaceAll('<', '\\u003c')
}

function escapeAttribute(value: string): string {
  return value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;')
}
