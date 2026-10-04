<script setup lang="ts">
import { onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import Icon from './Icon.vue'
import { data } from './home.data'
import { reducedMotion, useHome } from './home'

const PERIOD = 9000

const { t, href } = useHome()
const tour = ref<HTMLElement>()
const index = ref(0)
const loaded = reactive(new Set([0]))
const auto = ref(false)
const paused = ref(false)
const cycle = ref(0)
let timer = 0
let visible: IntersectionObserver | undefined
let narrow: MediaQueryList | undefined

function restart(): void {
  window.clearTimeout(timer)
  cycle.value++
  if (auto.value && !paused.value) timer = window.setTimeout(() => select(index.value + 1), PERIOD)
}

function select(next: number): void {
  const count = t.tour.stories.length
  index.value = (next + count) % count
  loaded.add(index.value)
  restart()
}

function onKey(event: KeyboardEvent): void {
  if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
  event.preventDefault()
  select(index.value + (event.key === 'ArrowDown' ? 1 : -1))
  tour.value?.querySelectorAll<HTMLElement>('.dc-tour__tab')[index.value]?.focus()
}

function pause(on: boolean): void {
  if (paused.value === on) return
  paused.value = on
  if (on) window.clearTimeout(timer)
  else restart()
}

function onFocusOut(event: FocusEvent): void {
  if (!tour.value?.contains(event.relatedTarget as Node)) pause(false)
}

function loadAllWhenStacked(): void {
  if (narrow?.matches) t.tour.stories.forEach((_, at) => loaded.add(at))
}

onMounted(() => {
  narrow = window.matchMedia('(max-width: 980px)')
  loadAllWhenStacked()
  narrow.addEventListener('change', loadAllWhenStacked)
  if (!tour.value || reducedMotion()) return
  for (const robot of tour.value.querySelectorAll<HTMLElement>('.dc-agent .dca-robot')) robot.dataset.motion = 'on'
  visible = new IntersectionObserver(
    ([entry]) => {
      auto.value = entry.isIntersecting && !narrow?.matches
      restart()
    },
    { threshold: 0.35 }
  )
  visible.observe(tour.value)
})

onBeforeUnmount(() => {
  window.clearTimeout(timer)
  visible?.disconnect()
  narrow?.removeEventListener('change', loadAllWhenStacked)
})
</script>

<template>
  <section class="dc-section" aria-labelledby="tour-title">
    <div class="dc-wrap">
      <header class="dc-head">
        <h2 id="tour-title">{{ t.tour.title }}</h2>
      </header>
      <div
        ref="tour"
        class="dc-tour"
        :style="{ '--tour-ms': `${PERIOD}ms` }"
        :data-auto="auto || undefined"
        :data-paused="paused || undefined"
        @pointerenter="pause(true)"
        @pointerleave="pause(false)"
        @focusin="pause(true)"
        @focusout="onFocusOut"
      >
        <div v-for="(story, at) in t.tour.stories" :key="story.name" class="dc-tour__item">
          <button
            :id="`tour-tab-${at}`"
            class="dc-tour__tab"
            type="button"
            :aria-controls="`tour-panel-${at}`"
            :aria-expanded="index === at"
            :tabindex="index === at ? 0 : -1"
            @click="select(at)"
            @keydown="onKey"
          >
            <span class="dc-tour__mark"><Icon :name="story.icon" :size="20" /></span>
            <span class="dc-tour__copy">
              <span class="dc-tour__name">{{ story.name }}</span>
              <span class="dc-tour__line">{{ story.line }}</span>
              <span v-if="story.team" class="dc-tour__team" aria-hidden="true">
                <span v-for="(agent, member) in data.agents" :key="member" class="dc-agent" v-html="agent" />
              </span>
            </span>
            <span v-if="index === at" :key="cycle" class="dc-tour__progress" aria-hidden="true" />
          </button>
          <div
            :id="`tour-panel-${at}`"
            class="dc-tour__panel"
            role="region"
            :aria-labelledby="`tour-tab-${at}`"
            :data-active="index === at || undefined"
          >
            <div class="dc-shot">
              <img
                :src="loaded.has(at) ? story.media.src : undefined"
                :alt="story.media.alt"
                :width="story.media.width"
                :height="story.media.height"
                loading="lazy"
              />
            </div>
            <div class="dc-tour__links">
              <span>{{ t.tour.readMore }}</span>
              <a
                v-for="link in story.links"
                :key="link.href"
                class="dc-button"
                data-variant="secondary"
                data-size="sm"
                :href="href(link.href)"
              >{{ link.text }}</a>
            </div>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
