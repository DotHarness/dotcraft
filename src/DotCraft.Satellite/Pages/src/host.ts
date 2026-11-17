interface WebViewBridge {
  postMessage(message: unknown): void
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewBridge }
  }
}

export const post = (message: unknown) => window.chrome?.webview?.postMessage(message)

export function listen<T>(receive: (state: T) => void): () => void {
  const listener = (event: { data: unknown }) => receive(event.data as T)
  window.chrome?.webview?.addEventListener('message', listener)
  post({ type: 'ready' })
  return () => window.chrome?.webview?.removeEventListener('message', listener)
}
