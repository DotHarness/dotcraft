<script setup lang="ts">
import { computed, onMounted, ref, watchEffect } from 'vue'
import { useData } from 'vitepress'
import { useDocStrings } from './docPage'

const { theme, page, lang } = useData()
const strings = useDocStrings()
const updated = ref('')
const isoUpdated = computed(() => (page.value.lastUpdated ? new Date(page.value.lastUpdated).toISOString() : ''))

onMounted(() => {
  watchEffect(() => {
    updated.value = page.value.lastUpdated
      ? new Intl.DateTimeFormat(lang.value, { dateStyle: 'medium' }).format(new Date(page.value.lastUpdated))
      : ''
  })
})
</script>

<template>
  <p v-if="isoUpdated" class="dc-doc-meta">{{ theme.lastUpdated?.text ?? strings.lastUpdated }} <time :datetime="isoUpdated">{{ updated }}</time></p>
</template>
