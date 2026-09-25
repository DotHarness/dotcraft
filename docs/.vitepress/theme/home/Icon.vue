<script setup lang="ts">
import { computed } from 'vue'
import { data } from './home.data'

const props = withDefaults(defineProps<{ name: string; size?: number; stroke?: number }>(), { size: 16, stroke: 1.8 })
const parts = computed(() => data.icons[props.name])
</script>

<template>
  <svg
    v-if="parts.solid"
    :viewBox="parts.viewBox"
    :width="size"
    :height="size"
    fill="currentColor"
    aria-hidden="true"
    v-html="parts.body"
  />
  <svg
    v-else
    :viewBox="parts.viewBox"
    :width="size"
    :height="size"
    fill="none"
    stroke="currentColor"
    :stroke-width="stroke"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    v-html="parts.body"
  />
</template>
