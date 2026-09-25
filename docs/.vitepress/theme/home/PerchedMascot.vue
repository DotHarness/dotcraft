<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import type { Pose } from './home.data'
import { reducedMotion } from './home'
import { bringToLife } from './mascotLife'

const props = defineProps<{ perch: 'hero' | 'close'; poses: Record<Pose, string>; size: number }>()

const mascot = ref<HTMLElement>()
const look = ref<Pose>('idle')
let alive = false
let settle = 0
let stop: (() => void) | undefined

function show(pose: Pose, ms: number): void {
  if (!alive) return
  look.value = pose
  window.clearTimeout(settle)
  settle = window.setTimeout(() => {
    look.value = 'idle'
  }, ms)
}

function greet(): void {
  if (look.value === 'idle') show('greeting', 1900)
}

defineExpose({ celebrate: () => show('done', 1600) })

onMounted(() => {
  if (!mascot.value || reducedMotion()) return
  alive = true
  stop = bringToLife(mascot.value)
})

onBeforeUnmount(() => {
  window.clearTimeout(settle)
  stop?.()
})
</script>

<template>
  <div class="dc-perch" :class="`dc-perch--${props.perch}`" :style="{ '--size': `${size}px` }" aria-hidden="true">
    <span class="dc-perch__shadow" />
    <span ref="mascot" class="dc-mascot" :data-look="look" @pointerenter="greet">
      <span v-for="(markup, pose) in poses" :key="pose" class="dc-mascot__look" :data-pose="pose" v-html="markup" />
    </span>
  </div>
</template>
