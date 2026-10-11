import {
  MCP_APP_SANDBOX_PROXY_READY_METHOD,
  MCP_APP_SANDBOX_RESOURCE_READY_METHOD,
  MCP_APP_SANDBOX_SCHEME,
  VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD
} from '../../../../desktop/src/shared/mcpAppSandbox'

const PROXY_HTML = `<!doctype html><html><head><meta charset="utf-8"></head><body style="margin:0;background:transparent"><script>
(() => {
  let inner = null;
  addEventListener('message', (event) => {
    if (event.source === parent) {
      const message = event.data;
      if (message && (message.method === ${JSON.stringify(VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD)} || message.method === ${JSON.stringify(MCP_APP_SANDBOX_RESOURCE_READY_METHOD)})) {
        inner = document.createElement('iframe');
        inner.setAttribute('sandbox', 'allow-scripts');
        inner.setAttribute('referrerpolicy', 'no-referrer');
        inner.style.cssText = 'display:block;width:100%;height:100vh;border:0;background:transparent';
        inner.srcdoc = String(message.params && message.params.html || '');
        document.body.replaceChildren(inner);
        return;
      }
      if (inner && inner.contentWindow) inner.contentWindow.postMessage(message, '*', Array.from(event.ports));
      return;
    }
    if (inner && event.source === inner.contentWindow) parent.postMessage(event.data, '*');
  });
  parent.postMessage({ jsonrpc: '2.0', method: ${JSON.stringify(MCP_APP_SANDBOX_PROXY_READY_METHOD)}, params: {} }, '*');
})();
</script></body></html>`

// Desktop points sandboxed frames at a custom Electron scheme; the browser serves the same relay inline.
export function installSandboxProxy(): void {
  const descriptor = Object.getOwnPropertyDescriptor(HTMLIFrameElement.prototype, 'src')
  if (!descriptor?.get || !descriptor.set) return
  const { get, set } = descriptor
  Object.defineProperty(HTMLIFrameElement.prototype, 'src', {
    configurable: true,
    enumerable: descriptor.enumerable,
    get() {
      return get.call(this)
    },
    set(value: string) {
      if (typeof value === 'string' && value.startsWith(`${MCP_APP_SANDBOX_SCHEME}:`)) this.srcdoc = PROXY_HTML
      else set.call(this, value)
    }
  })
}
