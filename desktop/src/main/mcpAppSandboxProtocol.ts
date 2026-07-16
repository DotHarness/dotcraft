import { protocol } from 'electron'
import {
  MCP_APP_MAX_BRIDGE_MESSAGE_BYTES,
  MCP_APP_MAX_RESOURCE_BYTES,
  MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD,
  MCP_APP_SANDBOX_PROXY_READY_METHOD,
  MCP_APP_SANDBOX_PROXY_URL,
  MCP_APP_SANDBOX_RESOURCE_READY_METHOD,
  MCP_APP_SANDBOX_SCHEME
} from '../shared/mcpAppSandbox'

let defaultProtocolHandlerInstalled = false

export const MCP_APP_SANDBOX_PROXY_HTML = `<!doctype html><html><head><meta charset="utf-8"></head><body style="margin:0;background:transparent"><script>
(() => {
  let inner = null;
  const maxBytes = ${MCP_APP_MAX_BRIDGE_MESSAGE_BYTES};
  const maxResourceBytes = ${MCP_APP_MAX_RESOURCE_BYTES};
  const resourceReadyMethod = ${JSON.stringify(MCP_APP_SANDBOX_RESOURCE_READY_METHOD)};
  const byteLength = (value) => new TextEncoder().encode(value).byteLength;
  const withinLimit = (message) => {
    try {
      const json = JSON.stringify(message);
      return json !== undefined && byteLength(json) <= maxBytes;
    }
    catch { return false; }
  };
  const resourceBootstrapWithinLimit = (message) => {
    if (!message || typeof message !== 'object' || message.method !== resourceReadyMethod) return false;
    const params = message.params;
    if (!params || typeof params !== 'object' || typeof params.html !== 'string') return false;
    if (byteLength(params.html) > maxResourceBytes) return false;
    return withinLimit({ ...message, params: { ...params, html: '' } });
  };
  const violate = () => {
    if (inner) inner.remove();
    inner = null;
    window.parent.postMessage({ jsonrpc: '2.0', method: ${JSON.stringify(MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD)}, params: {} }, '*');
  };
  const forward = (message) => {
    if (!withinLimit(message)) { violate(); return; }
    if (inner && inner.contentWindow) inner.contentWindow.postMessage(message, '*');
  };
  window.addEventListener('message', (event) => {
    if (event.source === window.parent) {
      const message = event.data;
      if (message && message.method === resourceReadyMethod) {
        if (!resourceBootstrapWithinLimit(message)) { violate(); return; }
        const params = message.params || {};
        inner = document.createElement('iframe');
        inner.setAttribute('sandbox', 'allow-scripts');
        inner.setAttribute('referrerpolicy', 'no-referrer');
        inner.style.cssText = 'display:block;width:100%;height:100vh;border:0;background:transparent';
        inner.srcdoc = String(params.html || '');
        document.body.replaceChildren(inner);
        return;
      }
      if (!withinLimit(message)) { violate(); return; }
      forward(message);
      return;
    }
    if (inner && event.source === inner.contentWindow) {
      if (!withinLimit(event.data)) { violate(); return; }
      window.parent.postMessage(event.data, '*');
    }
  });
  window.parent.postMessage({ jsonrpc: '2.0', method: ${JSON.stringify(MCP_APP_SANDBOX_PROXY_READY_METHOD)}, params: {} }, '*');
})();
</script></body></html>`

export function registerMcpAppSandboxScheme(): void {
  protocol.registerSchemesAsPrivileged([
    {
      scheme: MCP_APP_SANDBOX_SCHEME,
      privileges: {
        standard: true,
        secure: true,
        supportFetchAPI: false,
        bypassCSP: false,
        stream: false,
        corsEnabled: false
      }
    }
  ])
}

export function installMcpAppSandboxProtocolHandler(): void {
  if (defaultProtocolHandlerInstalled) return
  defaultProtocolHandlerInstalled = true
  protocol.handle(MCP_APP_SANDBOX_SCHEME, handleMcpAppSandboxRequest)
}

export async function handleMcpAppSandboxRequest(request: Request): Promise<Response> {
  if (request.url !== MCP_APP_SANDBOX_PROXY_URL) {
    return new Response(null, { status: 404 })
  }
  // Do not add a CSP header here. A proxy CSP would also constrain the inner srcdoc and could
  // silently override the narrower, per-App policy injected by buildMcpAppDocument.
  return new Response(MCP_APP_SANDBOX_PROXY_HTML, {
    status: 200,
    headers: {
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': 'no-store'
    }
  })
}
