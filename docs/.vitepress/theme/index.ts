import DefaultTheme from 'vitepress/theme'
import type { EnhanceAppContext } from 'vitepress'
import './motion.css'
import './custom.css'
import './home-layout.css'
import './mascot.css'
import LiveMascot from './LiveMascot.vue'
import { setupDemoEmbed } from './demoEmbed'
import { setupDownloadButton } from './downloadButton'
import { setupHomeMotion } from './homeMotion'
import { setupLiveMascots } from './liveMascot'

export default {
  extends: DefaultTheme,
  enhanceApp({ app, router }: EnhanceAppContext) {
    // Global registration so the markdown HTML blocks can render it.
    app.component('LiveMascot', LiveMascot)

    if (typeof window === 'undefined') return

    const enhance = (): void => {
      setupDemoEmbed()
      setupDownloadButton()
      setupHomeMotion()
      setupLiveMascots()
    }

    // The page component can mount after enhanceApp, so retry until the hero
    // markup exists (both enhancers are idempotent). setTimeout, not rAF, so it
    // still runs when the tab loads hidden — rAF is paused while not visible.
    const initEmbed = (): void => {
      let attempts = 0
      const tick = (): void => {
        enhance()
        if (++attempts < 40 && !document.querySelector('[data-download], .dc-demo')) {
          setTimeout(tick, 50)
        }
      }
      tick()
    }

    const previous = router.onAfterRouteChange
    router.onAfterRouteChange = (to: string) => {
      previous?.(to)
      initEmbed()
    }
    initEmbed()
  }
}
