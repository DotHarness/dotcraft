<script setup lang="ts">
import { onMounted, ref } from 'vue'
import HomeClose from './HomeClose.vue'
import HomeHarness from './HomeHarness.vue'
import HomeHero from './HomeHero.vue'
import HomeProducts from './HomeProducts.vue'
import HomeTour from './HomeTour.vue'
import HomeWhy from './HomeWhy.vue'
import { homeCopy, type Locale } from './copy'
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
  </div>
</template>
