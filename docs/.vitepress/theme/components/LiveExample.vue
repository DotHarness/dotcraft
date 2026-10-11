<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useData } from 'vitepress'
import { BUILDER_HEIGHT, BUILDER_MIN_ZOOM, BUILDER_WIDTH, EXAMPLE_LABELS, findShowcase, LIVE_COPY, type LiveFeature } from './liveExample'

type AnswerMode = 'plain' | 'interactive'

const props = withDefaults(defineProps<{
  feature: LiveFeature
  examples?: string[]
  compare?: boolean
  height?: number
}>(), {
  examples: () => [],
  compare: false,
  height: 520
})

const { isDark, lang } = useData()
const locale = computed(() => (lang.value.startsWith('zh') ? 'zh' : 'en'))
const copy = computed(() => LIVE_COPY[locale.value])
const builder = computed(() => props.feature === 'agent-builder')
const root = ref<HTMLElement>()
const stage = ref<HTMLElement>()
const frame = ref<HTMLIFrameElement>()
const selected = ref(props.examples[0] ?? '')
const mode = ref<AnswerMode>('interactive')
const src = ref('')
const ready = ref(false)
const failed = ref(false)
const contentHeight = ref(props.height)
const width = ref(0)
const generation = ref(0)
let visibility: IntersectionObserver | undefined
let resize: ResizeObserver | undefined

const zoom = computed(() => Math.max(BUILDER_MIN_ZOOM, Math.min(1, width.value / BUILDER_WIDTH)))
const stageHeight = computed(() => (builder.value ? Math.round(BUILDER_HEIGHT * zoom.value) : contentHeight.value))
const frameStyle = computed(() => builder.value
  ? { width: `${BUILDER_WIDTH}px`, height: `${BUILDER_HEIGHT}px`, zoom: String(zoom.value) }
  : { width: '100%', height: `${contentHeight.value}px` })
const showBar = computed(() => props.examples.length > 1 || props.compare)
const frameTitle = computed(() => copy.value.frame.replace('{label}', builder.value ? copy.value.builder : label(selected.value)))

function label(id: string): string {
  return EXAMPLE_LABELS[id]?.[locale.value] ?? id
}

function post(message: Record<string, unknown>): void {
  frame.value?.contentWindow?.postMessage(message, '*')
}

function postTheme(): void {
  post({ type: 'dotcraft-demo:set-theme', theme: isDark.value ? 'dark' : 'light' })
}

function show(): void {
  post({ type: 'dotcraft-showcase:show', example: selected.value, mode: mode.value })
}

function select(id: string): void {
  selected.value = id
  show()
}

function setMode(next: AnswerMode): void {
  mode.value = next
  show()
}

function restart(): void {
  ready.value = false
  generation.value += 1
}

async function mount(): Promise<void> {
  if (src.value) return
  const page = await findShowcase()
  if (!page) {
    failed.value = true
    return
  }
  const query = new URLSearchParams({ feature: props.feature, theme: isDark.value ? 'dark' : 'light', lang: locale.value })
  if (selected.value) query.set('example', selected.value)
  if (props.compare) query.set('mode', mode.value)
  src.value = `${page}?${query}`
}

function onLoad(): void {
  postTheme()
  if (!builder.value) show()
}

function onMessage(event: MessageEvent): void {
  if (!frame.value || event.source !== frame.value.contentWindow) return
  const data = event.data as { type?: string; height?: unknown } | null
  if (data?.type === 'dotcraft-showcase:restart') {
    restart()
    return
  }
  if (data?.type !== 'dotcraft-showcase:height' || typeof data.height !== 'number' || data.height <= 0) return
  if (!builder.value) contentHeight.value = Math.ceil(data.height)
  ready.value = true
  if (builder.value) void nextTick(() => stage.value?.scrollTo({ left: stage.value.scrollWidth }))
}

watch(isDark, postTheme)

onMounted(() => {
  window.addEventListener('message', onMessage)
  if (!root.value) return
  resize = new ResizeObserver((entries) => {
    width.value = entries[0]?.contentRect.width ?? 0
  })
  resize.observe(root.value)
  visibility = new IntersectionObserver((entries) => {
    if (!entries.some((entry) => entry.isIntersecting)) return
    visibility?.disconnect()
    void mount()
  }, { rootMargin: '400px 0px' })
  visibility.observe(root.value)
})

onBeforeUnmount(() => {
  window.removeEventListener('message', onMessage)
  visibility?.disconnect()
  resize?.disconnect()
})
</script>

<template>
  <figure ref="root" class="dc-live" :data-builder="builder || undefined" :data-ready="ready || undefined">
    <div v-if="showBar" class="dc-live__bar">
      <div v-if="examples.length > 1" class="dc-live__chips" role="group" :aria-label="copy.examples">
        <button
          v-for="id in examples"
          :key="id"
          type="button"
          class="dc-live__chip"
          :aria-pressed="id === selected"
          @click="select(id)"
        >{{ label(id) }}</button>
      </div>
      <div v-if="compare" class="dc-live__modes" role="group" :aria-label="copy.answerStyle">
        <button type="button" :aria-pressed="mode === 'plain'" @click="setMode('plain')">{{ copy.plain }}</button>
        <button type="button" :aria-pressed="mode === 'interactive'" @click="setMode('interactive')">{{ copy.interactive }}</button>
      </div>
    </div>
    <div ref="stage" class="dc-live__stage" :style="{ height: `${stageHeight}px` }">
      <div v-if="!ready" class="dc-live__placeholder" role="status">
        <span v-if="failed" class="dc-live__failed">{{ copy.unavailable }}</span>
        <template v-else>
          <span class="dc-live__line dc-live__line--prompt" />
          <span class="dc-live__line" />
          <span class="dc-live__block" />
          <span class="dc-live__sr">{{ copy.loading }}</span>
        </template>
      </div>
      <iframe
        v-if="src"
        :key="generation"
        ref="frame"
        class="dc-live__frame"
        :src="src"
        :title="frameTitle"
        :style="frameStyle"
        allow="clipboard-write"
        @load="onLoad"
      />
    </div>
  </figure>
</template>
