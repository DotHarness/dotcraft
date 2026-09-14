import { randomUUID } from 'node:crypto'
import type { WebContents } from 'electron'
import type { BrowserPageReference, BrowserSelectionKind } from '../shared/viewer/browserFeedback'
import { selectionScript, type SelectionResult } from '../shared/viewer/browserSelectionScript'

export async function capturePageReference(
  page: WebContents,
  origin: { tabId: string; threadId?: string },
  kind: BrowserSelectionKind,
  accent?: string
): Promise<BrowserPageReference | null> {
  const url = page.getURL()
  const title = page.getTitle()
  const selected: SelectionResult | null = await page.executeJavaScript(selectionScript(kind, accent))
  if (!selected || (kind === 'text' && !selected.text.trim())) return null
  if (page.isDestroyed() || page.getURL() !== url) return null
  const reference: BrowserPageReference = { id: randomUUID(), ...origin, kind: selected.kind ?? kind, url, title, text: selected.text }
  if (selected.rect) {
    if (selected.rect.width <= 0 || selected.rect.height <= 0) return null
    await page.executeJavaScript('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')
    if (page.isDestroyed() || page.getURL() !== url) return null
    const zoom = page.getZoomFactor()
    const rect = {
      x: Math.max(0, Math.round(selected.rect.x * zoom)),
      y: Math.max(0, Math.round(selected.rect.y * zoom)),
      width: Math.max(1, Math.round(selected.rect.width * zoom)),
      height: Math.max(1, Math.round(selected.rect.height * zoom))
    }
    const image = await page.capturePage(rect)
    reference.imageDataUrl = image.toDataURL()
    reference.bounds = rect
    reference.previewDataUrl = (await page.capturePage()).toDataURL()
  }
  return reference
}
