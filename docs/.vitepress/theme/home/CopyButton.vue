<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import Icon from './Icon.vue'

const props = defineProps<{ text: () => string; label: string; copy: string; copied: string; round?: boolean; tipBelow?: boolean }>()
const emit = defineEmits<{ copied: [] }>()
const done = ref(false)
let reset = 0

async function onClick(): Promise<void> {
  try {
    await navigator.clipboard.writeText(props.text().trim())
  } catch {
    return
  }
  done.value = true
  emit('copied')
  window.clearTimeout(reset)
  reset = window.setTimeout(() => {
    done.value = false
  }, 1600)
}

onBeforeUnmount(() => window.clearTimeout(reset))
</script>

<template>
  <button
    type="button"
    class="dc-icon-button dc-copy"
    :class="{ 'dc-icon-button--round': round }"
    :aria-label="label"
    :data-copied="done || undefined"
    @click="onClick"
  >
    <Icon v-if="done" class="dc-icon-button__done" name="check" :size="15" :stroke="2.2" />
    <Icon v-else name="copy" :size="15" :stroke="2" />
    <span class="dc-tip" :class="{ 'dc-tip--above': !tipBelow }" aria-live="polite">{{ done ? copied : copy }}</span>
  </button>
</template>
