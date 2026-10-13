<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import LiveExample from './LiveExample.vue'
import { LIVE_FEATURES, type LiveFeature } from './liveExample'
import { useScrollSteps } from './scrollSteps'

interface TourStep {
  example: string
  chapter: string
  title: string
  body: string
}

const props = defineProps<{
  feature: LiveFeature
  steps: TourStep[]
}>()

const section = ref<HTMLElement>()
const pin = ref<HTMLElement>()
const head = ref<HTMLElement>()
const { index, pinned, goTo } = useScrollSteps(section, pin, () => props.steps.length)
const step = computed(() => props.steps[index.value] ?? props.steps[0])
const side = computed(() => LIVE_FEATURES[props.feature].frame === 'phone')
const natural = ref(0)
const room = ref(0)
let resize: ResizeObserver | undefined

const GAP = 20
const zoom = computed(() => {
  if (!pinned.value || natural.value <= 0 || room.value <= 0) return 1
  return Math.max(0.55, Math.min(1, room.value / natural.value))
})

function measure(): void {
  if (!pin.value || !head.value) return
  room.value = pin.value.clientHeight - (side.value ? 0 : head.value.offsetHeight) - GAP * 2
}

onMounted(() => {
  resize = new ResizeObserver(measure)
  if (pin.value) resize.observe(pin.value)
  if (head.value) resize.observe(head.value)
})

onBeforeUnmount(() => resize?.disconnect())
</script>

<template>
  <section
    ref="section"
    class="dc-walk"
    :data-pinned="pinned || undefined"
    :data-layout="side ? 'side' : undefined"
    :style="{ '--walk-steps': steps.length }"
  >
    <div ref="pin" class="dc-walk__pin">
      <div ref="head" class="dc-walk__head">
        <div class="dc-walk__nav" role="group">
          <button
            v-for="(item, at) in steps"
            :key="at"
            type="button"
            class="dc-walk__step"
            :aria-pressed="at === index"
            @click="goTo(at)"
          >
            <span class="dc-walk__num">{{ at + 1 }}</span>
            <span class="dc-walk__label">
              <span>{{ item.title }}</span>
              <span v-if="side" class="dc-walk__body">{{ item.body }}</span>
            </span>
          </button>
        </div>
        <div v-if="!side" class="dc-walk__cards" aria-live="polite">
          <p v-for="(item, at) in steps" :key="at" class="dc-walk__card" :data-active="at === index || undefined">{{ item.body }}</p>
        </div>
      </div>
      <div class="dc-walk__stage" :style="{ zoom }">
        <LiveExample
          :feature="feature"
          :examples="steps.map((item) => item.example)"
          :active="step.example"
          :chapter="step.chapter"
          @height="natural = $event"
        />
      </div>
    </div>
  </section>
</template>
