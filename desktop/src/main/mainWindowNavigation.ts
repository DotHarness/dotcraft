import type { WebContents } from 'electron'

type GuardedContents = Pick<WebContents, 'getURL' | 'on' | 'setWindowOpenHandler'>

export function guardMainWindowNavigation(contents: GuardedContents): void {
  contents.setWindowOpenHandler(() => ({ action: 'deny' }))
  contents.on('will-navigate', (event, url) => {
    if (!isSameDocument(contents.getURL(), url)) event.preventDefault()
  })
}

function isSameDocument(current: string, next: string): boolean {
  return current.split('#')[0] === next.split('#')[0]
}
