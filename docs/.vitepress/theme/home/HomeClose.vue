<script setup lang="ts">
import { ref } from 'vue'
import CopyButton from './CopyButton.vue'
import DownloadSplit from './DownloadSplit.vue'
import InstallTabs from './InstallTabs.vue'
import PerchedMascot from './PerchedMascot.vue'
import { installCommands } from './copy'
import { data } from './home.data'
import { useHome } from './home'

const { t, os, href } = useHome()
const mascot = ref<InstanceType<typeof PerchedMascot>>()
</script>

<template>
  <section class="dc-close" aria-labelledby="close-title">
    <div class="dc-wrap">
      <h2 id="close-title">{{ t.close.title }}</h2>
      <div class="dc-close__composer">
        <PerchedMascot ref="mascot" perch="close" :poses="data.mascots.close" :size="72" />
        <div class="dc-composer">
          <span class="dc-composer__glow" aria-hidden="true" />
          <div class="dc-composer__input">
            <code
              v-for="(command, key) in installCommands"
              :id="`close-cmd-${key}`"
              :key="key"
              role="tabpanel"
              :aria-labelledby="`close-tab-${key}`"
              :hidden="os !== key"
            >{{ command }}</code>
          </div>
          <div class="dc-composer__foot">
            <InstallTabs id="close-tab" v-model="os" :label="t.install.tablist" :controls="(value) => `close-cmd-${value}`" />
            <CopyButton
              round
              :text="() => installCommands[os]"
              :label="t.install.copyLabel"
              :copy="t.install.copy"
              :copied="t.install.copied"
              @copied="mascot?.celebrate()"
            />
          </div>
        </div>
      </div>
      <div class="dc-close__context">
        <a class="dc-button" data-variant="primary" data-size="prominent" :href="href('/getting-started')">{{ t.start }}</a>
        <DownloadSplit drop="up" />
      </div>
    </div>
  </section>
</template>
