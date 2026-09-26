import DefaultTheme from 'vitepress/theme-without-fonts'
import { h } from 'vue'
import '../../../desktop/src/renderer/styles/foundations/tokens.css'
import '../../../desktop/src/renderer/styles/foundations/themes.css'
import './tokens.css'
import './chrome.css'
import './mobile-nav.css'
import './sidebar.css'
import './band.css'
import './doc.css'
import './code.css'
import './search.css'
import DocBand from './components/DocBand.vue'
import DocMeta from './components/DocMeta.vue'
import DocOutlineActions from './components/DocOutlineActions.vue'
import { setupCodeWrap } from './codeWrap'

export default {
  extends: DefaultTheme,
  Layout: () =>
    h(DefaultTheme.Layout, null, {
      'doc-top': () => h(DocBand),
      'aside-outline-after': () => h(DocOutlineActions),
      'doc-footer-before': () => h(DocMeta)
    }),
  enhanceApp() {
    if (typeof window !== 'undefined') setupCodeWrap()
  }
}
