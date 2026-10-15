<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { startWelcomeBackdrop, type WelcomeBackdropHandle } from '../../../../desktop/src/renderer/components/conversation/welcomeBackdrop/backdropEngine'
import { THEME_CHANGED_EVENT } from '../../../../desktop/src/shared/theme'
import { reducedMotion } from './home'

const FIELD_CONTRAST = { dark: 2.2, light: 1.8 }

const root = ref<HTMLElement>()
const shown = ref(false)
let field: WelcomeBackdropHandle | null = null
let theme: MutationObserver | undefined

onMounted(() => {
  const host = root.value
  if (!host) return
  field = startWelcomeBackdrop(host, {
    still: reducedMotion(),
    canvasClassName: 'dc-glyphs__canvas',
    contrast: FIELD_CONTRAST,
    calm: () => host.parentElement?.querySelector<HTMLElement>(':scope > .dc-wrap') ?? null,
    onShown: () => {
      shown.value = true
    }
  })
  theme = new MutationObserver(() => window.dispatchEvent(new Event(THEME_CHANGED_EVENT)))
  theme.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
})

onBeforeUnmount(() => {
  theme?.disconnect()
  field?.destroy()
})
</script>

<template>
  <div ref="root" class="dc-glyphs" :data-shown="shown || undefined" aria-hidden="true" />
</template>
