<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { reducedMotion } from './home'
import { bringToLife, setPose, type Pose } from './mascotLife'

const props = defineProps<{ perch: 'hero' | 'close'; markup: string; size: number }>()

const mascot = ref<HTMLElement>()
let look: Pose = 'idle'
let alive = false
let settle = 0
let stop: (() => void) | undefined

function show(pose: Pose, ms: number): void {
  if (!alive || !mascot.value) return
  look = pose
  setPose(mascot.value, pose)
  window.clearTimeout(settle)
  settle = window.setTimeout(() => {
    look = 'idle'
    if (mascot.value) setPose(mascot.value, 'idle')
  }, ms)
}

function greet(): void {
  if (look === 'idle') show('greeting', 1900)
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
    <span ref="mascot" class="dc-mascot" @pointerenter="greet" v-html="markup" />
  </div>
</template>
