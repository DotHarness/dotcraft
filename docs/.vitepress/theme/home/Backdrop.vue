<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { animateBackdrop } from './backdrop'
import { reducedMotion } from './home'

const root = ref<HTMLElement>()
let stop: (() => void) | undefined

onMounted(() => {
  if (root.value && !reducedMotion()) stop = animateBackdrop(root.value)
})

onBeforeUnmount(() => stop?.())
</script>

<template>
  <div ref="root" class="dc-backdrop" aria-hidden="true" />
</template>
