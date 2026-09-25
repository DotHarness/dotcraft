<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useData } from 'vitepress'
import Icon from './Icon.vue'
import { reducedMotion, useHome } from './home'
import { demoRendered, findDemo, fitDemo, MIN_EMBED_VIEWPORT, setDemoTheme } from './demoEmbed'

const READY_TIMEOUT = 15000

const { locale, t } = useHome()
const { isDark } = useData()
const container = ref<HTMLElement>()
const frame = ref<HTMLIFrameElement>()
const src = ref('')
const ready = ref(false)
const active = ref(false)
const optIn = ref(false)
let resize: ResizeObserver | undefined
let readyTimer = 0

async function mount(): Promise<void> {
  if (src.value) return
  const page = await findDemo()
  if (page) src.value = `${page}?theme=${isDark.value ? 'dark' : 'light'}&lang=${locale}`
}

function fit(): void {
  if (container.value && frame.value) fitDemo(container.value, frame.value)
}

function onLoad(): void {
  fit()
  const started = Date.now()
  const settle = (): void => {
    const painted = demoRendered(frame.value) || Date.now() - started > READY_TIMEOUT
    readyTimer = window.setTimeout(() => {
      if (!painted) return settle()
      setDemoTheme(frame.value, isDark.value)
      ready.value = true
    }, painted ? 250 : 100)
  }
  settle()
}

function launch(): void {
  if (src.value) {
    active.value = true
    return
  }
  optIn.value = false
  void mount()
}

function onDocumentClick(event: MouseEvent): void {
  if (!container.value?.contains(event.target as Node)) active.value = false
}

function onDocumentKey(event: KeyboardEvent): void {
  if (event.key === 'Escape') active.value = false
}

watch(frame, fit, { flush: 'post' })
watch(isDark, (dark) => setDemoTheme(frame.value, dark))

onMounted(() => {
  const wide = window.matchMedia(`(min-width: ${MIN_EMBED_VIEWPORT}px)`).matches
  if (wide && !reducedMotion()) {
    if (document.readyState === 'complete') void mount()
    else window.addEventListener('load', () => void mount(), { once: true })
  } else {
    optIn.value = true
  }
  resize = new ResizeObserver(fit)
  if (container.value) resize.observe(container.value)
  document.addEventListener('click', onDocumentClick)
  document.addEventListener('keydown', onDocumentKey)
})

onBeforeUnmount(() => {
  window.clearTimeout(readyTimer)
  resize?.disconnect()
  document.removeEventListener('click', onDocumentClick)
  document.removeEventListener('keydown', onDocumentKey)
})
</script>

<template>
  <div ref="container" class="dc-demo" :data-ready="ready || undefined" :data-active="active || undefined">
    <img :src="t.demo.poster" :alt="t.demo.alt" width="1393" height="791" />
    <iframe v-if="src" ref="frame" class="dc-demo__frame" :title="t.demo.frame" :src="src" @load="onLoad" />
    <button v-if="!active" class="dc-button dc-demo__launch" data-variant="outline" type="button" @click.stop="launch">
      <Icon name="mouse-pointer-2" :size="15" :stroke="2" />
      <span>{{ optIn ? t.demo.load : t.demo.try }}</span>
    </button>
  </div>
</template>
