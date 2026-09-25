<script setup lang="ts">
import { onMounted, ref } from 'vue'
import HomeClose from './HomeClose.vue'
import HomeHarness from './HomeHarness.vue'
import HomeHero from './HomeHero.vue'
import HomeProducts from './HomeProducts.vue'
import HomeTour from './HomeTour.vue'
import HomeWhy from './HomeWhy.vue'
import Icon from './Icon.vue'
import { homeCopy, projectLinks, type Locale } from './copy'
import { localHref, provideHome, type InstallOs } from './home'
import './home.css'
import './controls.css'
import './stage.css'
import './sections.css'
import './harness.css'

const props = defineProps<{ locale: Locale }>()
const t = homeCopy[props.locale]
const os = ref<InstallOs>('windows')

provideHome({ locale: props.locale, t, os, href: (path) => localHref(props.locale, path) })

onMounted(() => {
  const ua = navigator.userAgent
  if (/Mac|Linux/.test(ua) && !/Windows|Android/.test(ua)) os.value = 'unix'
})
</script>

<template>
  <div class="dc-sheet">
    <HomeHero />
    <HomeWhy />
    <HomeTour />
    <HomeProducts />
    <HomeHarness />
    <HomeClose />
    <footer class="dc-foot">
      <span>{{ t.footer.copyright }}</span>
      <nav :aria-label="t.footer.label">
        <a v-for="link in projectLinks" :key="link.href" class="dc-button" data-variant="ghost" data-size="sm" :href="link.href">
          <Icon v-if="link.icon" :name="link.icon" :size="14" />
          {{ link.text }}
        </a>
      </nav>
    </footer>
  </div>
</template>
