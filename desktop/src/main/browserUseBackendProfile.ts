export interface BrowserUseCapabilityInfo {
  id: string
  description: string
  docs: string
}

export interface BrowserUseBackendProfile {
  id: 'iab' | 'extension'
  name: string
  browserCapabilities: BrowserUseCapabilityInfo[]
  tabCapabilities: BrowserUseCapabilityInfo[]
  unsupportedDocs: string[]
  notes: string[]
  presentsInViewer: boolean
  enforcesNavigationPolicy: boolean
  separatesUserTabs: boolean
}

function viewportCapability(description: string): BrowserUseCapabilityInfo {
  return { id: 'viewport', description, docs: 'docs/capabilities/browser/viewport.md' }
}

const PAGE_ASSETS_CAPABILITY: BrowserUseCapabilityInfo = {
  id: 'pageAssets',
  description: 'Inventory and bundle file assets observed in the current rendered page state.',
  docs: 'docs/capabilities/tab/pageAssets.md'
}

const UNSUPPORTED_DOCS = ['browser.user.history', 'ordinaryDownloads', 'fileUpload', 'fileChooser', 'tab_content_export']

const NOTES = [
  'pageAssets.bundle is supported through Desktop file-transfer approval and temp output.',
  'Text and JSON browser results are capped at 1MB; screenshots are exempt.'
]

export const IAB_BACKEND_PROFILE: BrowserUseBackendProfile = {
  id: 'iab',
  name: 'DotCraft In-App Browser',
  browserCapabilities: [
    viewportCapability('Set or reset the embedded browser viewport size.'),
    {
      id: 'visibility',
      description: 'Show or hide the embedded browser surface.',
      docs: 'docs/capabilities/browser/visibility.md'
    }
  ],
  tabCapabilities: [PAGE_ASSETS_CAPABILITY],
  unsupportedDocs: UNSUPPORTED_DOCS,
  notes: NOTES,
  presentsInViewer: true,
  enforcesNavigationPolicy: true,
  separatesUserTabs: false
}

export const CHROME_BACKEND_PROFILE: BrowserUseBackendProfile = {
  id: 'extension',
  name: 'Chrome',
  browserCapabilities: [viewportCapability('Set or reset the Chrome tab viewport size.')],
  tabCapabilities: [PAGE_ASSETS_CAPABILITY],
  unsupportedDocs: UNSUPPORTED_DOCS,
  notes: NOTES,
  presentsInViewer: false,
  enforcesNavigationPolicy: false,
  separatesUserTabs: true
}
