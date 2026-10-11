function installHostActions(host) {
  let errorReported = false
  const blobs = new Map()
  const createObjectURL = URL.createObjectURL
  const revokeObjectURL = URL.revokeObjectURL
  const nativeClick = HTMLElement.prototype.click

  const describe = value => {
    if (value instanceof Error) return value.message ? `${value.name}: ${value.message}` : value.name
    if (typeof value === 'string') return value
    try {
      return JSON.stringify(value) ?? String(value)
    } catch {
      return String(value)
    }
  }

  const reportError = value => {
    if (errorReported) return
    errorReported = true
    host.send('visualization/error', { message: (describe(value) || 'Unknown error').slice(0, 1000) })
  }

  addEventListener('error', event => {
    if (!event.error && /^ResizeObserver loop/.test(event.message ?? '')) return
    reportError(event.error ?? event.message)
  })
  addEventListener('unhandledrejection', event => reportError(event.reason))

  URL.createObjectURL = object => {
    const url = createObjectURL.call(URL, object)
    if (object instanceof Blob) blobs.set(url, object)
    return url
  }
  URL.revokeObjectURL = url => {
    blobs.delete(url)
    revokeObjectURL.call(URL, url)
  }

  const toBase64 = bytes => {
    let binary = ''
    for (let offset = 0; offset < bytes.length; offset += 0x8000) {
      binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000))
    }
    return btoa(binary)
  }

  const sendDownload = (name, mimeType, data, size) => {
    if (size > host.maxDownloadBytes) host.send(host.downloadMethod, { name, size })
    else host.send(host.downloadMethod, { name, mimeType, data })
  }

  const download = (anchor, href) => {
    const name = (anchor.getAttribute('download') || '').trim().slice(0, 200) || 'download'
    if (/^blob:/i.test(href)) {
      const blob = blobs.get(href)
      if (!blob) {
        host.send(host.downloadMethod, { name })
        return
      }
      if (blob.size > host.maxDownloadBytes) {
        host.send(host.downloadMethod, { name, size: blob.size })
        return
      }
      blob.arrayBuffer()
        .then(buffer => sendDownload(name, blob.type, toBase64(new Uint8Array(buffer)), buffer.byteLength))
        .catch(() => host.send(host.downloadMethod, { name }))
      return
    }
    const match = /^data:([^,]*?)(;base64)?,(.*)$/is.exec(href)
    if (!match) {
      host.send(host.downloadMethod, { name })
      return
    }
    const mimeType = match[1] || 'text/plain'
    try {
      if (match[2]) {
        const payload = match[3].replace(/\s+/g, '')
        sendDownload(name, mimeType, payload, Math.floor(payload.length * 3 / 4))
      } else {
        const bytes = new TextEncoder().encode(decodeURIComponent(match[3]))
        sendDownload(name, mimeType, bytes.length > host.maxDownloadBytes ? '' : toBase64(bytes), bytes.length)
      }
    } catch {
      host.send(host.downloadMethod, { name })
    }
  }

  const handleAnchor = anchor => {
    const raw = (anchor.getAttribute('href') ?? anchor.getAttribute('xlink:href'))?.trim()
    if (raw == null || raw === '' || raw.startsWith('#')) return false
    if (anchor.hasAttribute('download') && /^(blob|data):/i.test(raw)) {
      download(anchor, raw)
      return true
    }
    try {
      const url = new URL(raw)
      if (url.protocol === 'http:' || url.protocol === 'https:') host.send('visualization/openLink', { url: url.href })
    } catch {}
    return true
  }

  const anchorFrom = event => {
    for (const node of event.composedPath()) {
      if (node instanceof Element && node.localName === 'a') return node
    }
    return null
  }

  addEventListener('click', event => {
    if (event.defaultPrevented) return
    const anchor = anchorFrom(event)
    if (anchor && handleAnchor(anchor)) event.preventDefault()
  })

  Object.defineProperty(HTMLAnchorElement.prototype, 'click', {
    configurable: true,
    writable: true,
    value() {
      if (this.isConnected) {
        nativeClick.call(this)
        return
      }
      if (this.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))) handleAnchor(this)
    }
  })
}
