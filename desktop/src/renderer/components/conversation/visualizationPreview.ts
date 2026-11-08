import { buildVisualizationDocument } from './visualizationSecurity'

const PREVIEW_FRAGMENT = '<!--__VISUALIZATION_FRAGMENT__-->'

export function createPreviewTemplate(): string {
  const document = buildVisualizationDocument(PREVIEW_FRAGMENT, 'light', 'en', 'preview', {
    widgetState: null,
    displayMode: 'inline',
    maxHeight: 10_000
  }, { design: 'Design', designs: 'Designs', previousDesign: 'Previous design', nextDesign: 'Next design' })
  return document.replace('</head>', '<style>:root[data-theme=light]{color-scheme:light}:root[data-theme=dark]{color-scheme:dark}</style></head>')
}
