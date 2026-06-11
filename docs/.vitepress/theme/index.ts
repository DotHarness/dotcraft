import DefaultTheme from 'vitepress/theme'
import type { EnhanceAppContext } from 'vitepress'
import './custom.css'
import { setupDemoEmbed } from './demoEmbed'

export default {
  extends: DefaultTheme,
  enhanceApp({ router }: EnhanceAppContext) {
    if (typeof window === 'undefined') return

    const initEmbed = (): void => {
      // Wait for the new page's DOM before looking for the hero embed.
      requestAnimationFrame(() => setupDemoEmbed())
    }

    const previous = router.onAfterRouteChange
    router.onAfterRouteChange = (to: string) => {
      previous?.(to)
      initEmbed()
    }
    initEmbed()
  }
}
