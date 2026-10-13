<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useData } from 'vitepress'
import { EXAMPLE_LABELS, findPage, LIVE_COPY, LIVE_FEATURES, type LiveFeature } from './liveExample'

type AnswerMode = 'plain' | 'interactive'
type PlaybackState = 'playing' | 'paused' | 'ended'

const PHONE_BEZEL = 10

const props = withDefaults(defineProps<{
  feature: LiveFeature
  examples?: string[]
  compare?: boolean
  height?: number
  /** Set when a parent such as a scroll walkthrough drives the example; the example bar and playback controls hide. */
  active?: string
  chapter?: string
}>(), {
  examples: () => [],
  compare: false,
  height: 520
})
const emit = defineEmits<{ height: [number] }>()

const { isDark, lang } = useData()
const locale = computed(() => (lang.value.startsWith('zh') ? 'zh' : 'en'))
const copy = computed(() => LIVE_COPY[locale.value])
const spec = computed(() => LIVE_FEATURES[props.feature])
const builder = computed(() => props.feature === 'agent-builder')
const phone = computed(() => spec.value.frame === 'phone')
const root = ref<HTMLElement>()
const card = ref<HTMLElement>()
const stage = ref<HTMLElement>()
const frame = ref<HTMLIFrameElement>()
const driven = computed(() => props.active !== undefined)
const selected = ref(props.active ?? props.examples[0] ?? '')
const mode = ref<AnswerMode>('interactive')
const page = ref('')
const ready = ref(false)
const failed = ref(false)
const contentHeight = ref(props.height)
const width = ref(0)
const generation = ref(0)
const playback = ref<{ state: PlaybackState; progress: number } | null>(null)
const inView = ref(false)
const userPaused = ref(false)
const reducedMotion = ref(false)
let mountObserver: IntersectionObserver | undefined
let viewObserver: IntersectionObserver | undefined
let resize: ResizeObserver | undefined

const pad = computed(() => (spec.value.backdrop ? (width.value < 640 ? 12 : 28) : 0))
const bezel = computed(() => (phone.value ? PHONE_BEZEL : 0))
const zoom = computed(() => {
  const fixed = spec.value.fixed
  if (!fixed) return 1
  const available = width.value - 2 * pad.value - 2 * bezel.value
  return Math.max(fixed.minZoom, Math.min(1, available / fixed.width))
})
const stageHeight = computed(() => {
  const fixed = spec.value.fixed
  const inner = fixed ? Math.round(fixed.height * zoom.value) : contentHeight.value
  return failed.value ? 120 : inner + 2 * pad.value + 2 * bezel.value
})
const frameStyle = computed(() => {
  const fixed = spec.value.fixed
  return fixed
    ? { width: `${fixed.width}px`, height: `${fixed.height}px`, zoom: String(zoom.value) }
    : { width: '100%', height: `${contentHeight.value}px` }
})
const screenStyle = computed(() => {
  const fixed = spec.value.fixed
  return fixed ? { width: `${Math.round(fixed.width * zoom.value)}px`, height: `${Math.round(fixed.height * zoom.value)}px` } : undefined
})
const showBar = computed(() => !driven.value && (props.examples.length > 1 || props.compare))
const frameTitle = computed(() => copy.value.frame.replace('{label}', builder.value ? copy.value.builder : label(selected.value)))
const src = computed(() => {
  if (!page.value) return ''
  const query = new URLSearchParams({ feature: props.feature, theme: isDark.value ? 'dark' : 'light', lang: locale.value })
  if (selected.value) query.set(spec.value.exampleParam ?? 'example', selected.value)
  if (props.compare) query.set('mode', mode.value)
  if (props.chapter) query.set('chapter', props.chapter)
  return `${page.value}?${query}`
})
// Theme changes are posted to the running frame, so they must not reload it.
const frameSrc = ref('')

function label(id: string): string {
  return EXAMPLE_LABELS[id]?.[locale.value] ?? id
}

function post(message: Record<string, unknown>): void {
  frame.value?.contentWindow?.postMessage(message, '*')
}

function postTheme(): void {
  post({ type: 'dotcraft-demo:set-theme', theme: isDark.value ? 'dark' : 'light' })
}

function control(action: 'play' | 'pause' | 'restart'): void {
  post({ type: 'dotcraft-showcase:control', action })
}

function autoplay(): void {
  if (driven.value) return
  if (playback.value && playback.value.state === 'paused' && inView.value && !userPaused.value && !reducedMotion.value) control('play')
}

function show(): void {
  post({ type: 'dotcraft-showcase:show', example: selected.value, mode: mode.value, chapter: props.chapter })
}

function reload(): void {
  ready.value = false
  playback.value = null
  frameSrc.value = src.value
  generation.value += 1
}

function select(id: string): void {
  selected.value = id
  userPaused.value = false
  if (spec.value.reloadOnSelect) reload()
  else show()
}

function setMode(next: AnswerMode): void {
  mode.value = next
  show()
}

function togglePlayback(): void {
  const state = playback.value?.state
  if (state === 'playing') {
    userPaused.value = true
    control('pause')
  } else {
    userPaused.value = false
    control(state === 'ended' ? 'restart' : 'play')
  }
}

async function mount(): Promise<void> {
  if (page.value) return
  const found = await findPage(spec.value.page)
  if (!found) {
    failed.value = true
    return
  }
  page.value = found
  frameSrc.value = src.value
}

function onLoad(): void {
  postTheme()
  if (!spec.value.fixed) show()
}

function onMessage(event: MessageEvent): void {
  if (!frame.value || event.source !== frame.value.contentWindow) return
  const data = event.data as { type?: string; height?: unknown; state?: unknown; progress?: unknown } | null
  switch (data?.type) {
    case 'dotcraft-showcase:restart':
      reload()
      return
    case 'dotcraft-showcase:playback': {
      if (data.state !== 'playing' && data.state !== 'paused' && data.state !== 'ended') return
      const progress = typeof data.progress === 'number' ? Math.min(1, Math.max(0, data.progress)) : 0
      playback.value = { state: data.state, progress }
      // A paused report at the start comes from a fresh timeline, such as a newly selected example.
      if (data.state === 'paused' && progress === 0) autoplay()
      return
    }
    case 'dotcraft-showcase:ready':
      ready.value = true
      return
    case 'dotcraft-showcase:height':
      if (typeof data.height !== 'number' || data.height <= 0) return
      if (!spec.value.fixed) contentHeight.value = Math.ceil(data.height)
      ready.value = true
      if (builder.value) void nextTick(() => stage.value?.scrollTo({ left: stage.value.scrollWidth }))
  }
}

watch(isDark, postTheme)
watch(stageHeight, (value) => emit('height', value), { immediate: true })
watch(() => [props.active, props.chapter], () => {
  if (props.active === undefined) return
  const changed = selected.value !== props.active
  selected.value = props.active
  // A running app follows a new chapter by message; only a different example needs a fresh start.
  if (changed && spec.value.reloadOnSelect) reload()
  else show()
})
watch(inView, (visible) => {
  if (visible) autoplay()
  else if (playback.value?.state === 'playing') control('pause')
})

onMounted(() => {
  window.addEventListener('message', onMessage)
  reducedMotion.value = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (!root.value) return
  resize = new ResizeObserver((entries) => {
    width.value = entries[0]?.contentRect.width ?? 0
  })
  if (card.value) resize.observe(card.value)
  mountObserver = new IntersectionObserver((entries) => {
    if (!entries.some((entry) => entry.isIntersecting)) return
    mountObserver?.disconnect()
    void mount()
  }, { rootMargin: '400px 0px' })
  mountObserver.observe(root.value)
  viewObserver = new IntersectionObserver((entries) => {
    inView.value = entries.some((entry) => entry.isIntersecting)
  }, { threshold: 0.35 })
  viewObserver.observe(root.value)
})

onBeforeUnmount(() => {
  window.removeEventListener('message', onMessage)
  mountObserver?.disconnect()
  viewObserver?.disconnect()
  resize?.disconnect()
})
</script>

<template>
  <figure
    ref="root"
    class="dc-live"
    :data-builder="builder || undefined"
    :data-phone="phone || undefined"
    :data-frameless="spec.frame === 'none' || undefined"
    :data-backdrop="spec.backdrop || undefined"
    :data-ready="ready || undefined"
  >
    <div v-if="showBar" class="dc-live__bar">
      <div v-if="examples.length > 1" class="dc-live__tabs" role="group" :aria-label="copy.examples">
        <button
          v-for="id in examples"
          :key="id"
          type="button"
          class="dc-live__tab"
          :aria-pressed="id === selected"
          @click="select(id)"
        >{{ label(id) }}</button>
      </div>
      <div v-if="compare" class="dc-live__modes" role="group" :aria-label="copy.answerStyle">
        <button type="button" :aria-pressed="mode === 'plain'" @click="setMode('plain')">{{ copy.plain }}</button>
        <button type="button" :aria-pressed="mode === 'interactive'" @click="setMode('interactive')">{{ copy.interactive }}</button>
      </div>
    </div>
    <div ref="card" class="dc-live__card">
      <div ref="stage" class="dc-live__stage" :style="{ height: `${stageHeight}px`, padding: pad ? `${pad}px` : undefined }">
        <Transition name="dc-live-fade">
          <div v-if="!ready && (!spec.fixed || failed)" class="dc-live__placeholder" role="status">
            <span v-if="failed" class="dc-live__failed">{{ copy.unavailable }}</span>
            <template v-else>
              <span class="dc-live__line dc-live__line--prompt" />
              <span class="dc-live__line" />
              <span class="dc-live__block" />
              <span class="dc-live__sr">{{ copy.loading }}</span>
            </template>
          </div>
        </Transition>
        <div v-if="(spec.fixed && !failed) || frameSrc" class="dc-live__device">
          <div class="dc-live__screen" :style="screenStyle">
            <Transition name="dc-live-fade">
              <div v-if="spec.fixed && !ready" class="dc-live__placeholder" :data-shape="phone ? 'phone' : 'window'" role="status">
                <template v-if="phone">
                  <span class="dc-live__line dc-live__line--title" />
                  <span v-for="row in 6" :key="row" class="dc-live__row" />
                </template>
                <template v-else>
                  <span class="dc-live__rail"><span v-for="row in 5" :key="row" class="dc-live__line" /></span>
                  <span class="dc-live__main">
                    <span class="dc-live__line dc-live__line--prompt" />
                    <span class="dc-live__line" />
                    <span class="dc-live__block" />
                  </span>
                </template>
                <span class="dc-live__sr">{{ copy.loading }}</span>
              </div>
            </Transition>
            <iframe
              v-if="frameSrc"
              :key="generation"
              ref="frame"
              class="dc-live__frame"
              :src="frameSrc"
              :title="frameTitle"
              :style="frameStyle"
              allow="clipboard-write"
              @load="onLoad"
            />
          </div>
        </div>
      </div>
      <div v-if="playback && ready && !driven" class="dc-live__playback">
        <button
          type="button"
          class="dc-live__play"
          :aria-label="playback.state === 'playing' ? copy.pause : playback.state === 'ended' ? copy.replay : copy.play"
          @click="togglePlayback"
        >
          <svg v-if="playback.state === 'playing'" viewBox="0 0 24 24" width="14" height="14" fill="currentColor" aria-hidden="true"><rect x="6" y="5" width="4" height="14" rx="1" /><rect x="14" y="5" width="4" height="14" rx="1" /></svg>
          <svg v-else-if="playback.state === 'ended'" viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 12a9 9 0 1 0 3-6.7L3 8" /><path d="M3 3v5h5" /></svg>
          <svg v-else viewBox="0 0 24 24" width="14" height="14" fill="currentColor" aria-hidden="true"><path d="M8 5.5v13a1 1 0 0 0 1.5.86l10.5-6.5a1 1 0 0 0 0-1.72L9.5 4.64A1 1 0 0 0 8 5.5Z" /></svg>
        </button>
        <div
          class="dc-live__progress"
          role="progressbar"
          :aria-label="copy.progress"
          aria-valuemin="0"
          aria-valuemax="100"
          :aria-valuenow="Math.round(playback.progress * 100)"
        >
          <span :style="{ transform: `scaleX(${playback.progress})` }" />
        </div>
      </div>
    </div>
  </figure>
</template>
