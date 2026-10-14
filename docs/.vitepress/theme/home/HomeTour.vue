<script setup lang="ts">
import { onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useData } from 'vitepress'
import Icon from './Icon.vue'
import { data } from './home.data'
import { reducedMotion, useHome } from './home'
import { useScrollSteps } from '../components/scrollSteps'

const { t, href } = useHome()
const { isDark } = useData()
const mounted = ref(false)
const section = ref<HTMLElement>()
const pin = ref<HTMLElement>()
const tour = ref<HTMLElement>()
const count = (): number => t.tour.stories.length
const { index, pinned, goTo, stepProgress } = useScrollSteps(section, pin, count, '(min-width: 981px) and (min-height: 640px)')
const loaded = reactive(new Set([0]))
let narrow: MediaQueryList | undefined

watch(index, (at) => loaded.add(at))
// Pinned shots swap as the reader scrolls, so all of them load up front instead of popping in empty.
watch(pinned, (on) => {
  if (on) t.tour.stories.forEach((_, at) => loaded.add(at))
})

function select(next: number): void {
  const at = (next + count()) % count()
  loaded.add(at)
  goTo(at)
}

function onKey(event: KeyboardEvent): void {
  if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
  event.preventDefault()
  const next = (index.value + (event.key === 'ArrowDown' ? 1 : -1) + count()) % count()
  select(next)
  tour.value?.querySelectorAll<HTMLElement>('.dc-tour__tab')[next]?.focus({ preventScroll: true })
}

function loadAllWhenStacked(): void {
  if (narrow?.matches) t.tour.stories.forEach((_, at) => loaded.add(at))
}

onMounted(() => {
  mounted.value = true
  narrow = window.matchMedia('(max-width: 980px)')
  loadAllWhenStacked()
  narrow.addEventListener('change', loadAllWhenStacked)
  if (!tour.value || reducedMotion()) return
  for (const robot of tour.value.querySelectorAll<HTMLElement>('.dc-agent .dca-robot')) robot.dataset.motion = 'on'
})

onBeforeUnmount(() => {
  narrow?.removeEventListener('change', loadAllWhenStacked)
})
</script>

<template>
  <section class="dc-section" aria-labelledby="tour-title">
    <div class="dc-wrap">
      <div ref="section" class="dc-tour-scroll" :data-pinned="pinned || undefined" :style="{ '--tour-steps': t.tour.stories.length }">
        <div ref="pin" class="dc-tour-scroll__pin">
          <header class="dc-head">
            <h2 id="tour-title">{{ t.tour.title }}</h2>
          </header>
          <div ref="tour" class="dc-tour">
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
                <span v-if="pinned && index === at" class="dc-tour__progress" aria-hidden="true">
                  <span :style="{ transform: `scaleX(${stepProgress()})` }" />
                </span>
              </button>
              <div
                :id="`tour-panel-${at}`"
                class="dc-tour__panel"
                role="region"
                :aria-labelledby="`tour-tab-${at}`"
                :data-active="index === at || undefined"
              >
                <div class="dc-shot dc-field">
                  <img
                    :src="mounted && loaded.has(at) ? story.media[isDark ? 'dark' : 'light'] : undefined"
                    :alt="story.media.alt"
                    :width="story.media.width"
                    :height="story.media.height"
                    :loading="pinned ? 'eager' : 'lazy'"
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
      </div>
    </div>
  </section>
</template>
