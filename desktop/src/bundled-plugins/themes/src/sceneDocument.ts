export const SCENE_STATUS_METHOD = 'themes/scene-status'

export function svgDataUrl(svg: string): string {
  const text = new TextDecoder().decode(new TextEncoder().encode(svg))
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(text)}`
}

export function buildSceneDocument(sceneSource: string, nonce: string): string {
  const csp = [
    "default-src 'none'", "script-src 'unsafe-inline'", "style-src 'unsafe-inline'",
    'img-src data:', 'font-src data:', 'media-src data:', "connect-src 'none'", "worker-src 'none'",
    "frame-src 'none'", "object-src 'none'", "base-uri 'none'", "form-action 'none'"
  ].join('; ')
  const status = JSON.stringify({ method: SCENE_STATUS_METHOD, params: { nonce } }).replace(/</g, '\\u003c')
  const runtime = `(() => {
    const status = ${status};
    const send = state => parent.postMessage({ ...status, params: { ...status.params, state } }, '*');
    window.addEventListener('error', () => send('failed'));
    window.addEventListener('unhandledrejection', () => send('failed'));
    document.addEventListener('click', event => event.preventDefault(), true);
    document.addEventListener('submit', event => event.preventDefault(), true);
    window.addEventListener('load', () => requestAnimationFrame(() => send('ready')), { once: true });
  })();`
  const source = sceneSource.replace(/<\/?(?:html|head|body)\b[^>]*>/gi, '')
    .replace(/<!doctype[^>]*>/gi, '')
    .replace(/<meta\b[^>]*>/gi, '').replace(/<base\b[^>]*>/gi, '')
  return `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="${csp}"><style>html,body{width:100%;height:100%;margin:0;overflow:hidden;pointer-events:none}body>svg,body>canvas{display:block;width:100%;height:100%}*{box-sizing:border-box}</style><script>${runtime}</script></head><body>${source}</body></html>`
}
