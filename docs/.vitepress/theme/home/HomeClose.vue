<script setup lang="ts">
import { ref } from 'vue'
import Backdrop from './Backdrop.vue'
import CopyButton from './CopyButton.vue'
import DownloadSplit from './DownloadSplit.vue'
import Icon from './Icon.vue'
import InstallTabs from './InstallTabs.vue'
import PerchedMascot from './PerchedMascot.vue'
import { installCommands, projectLinks } from './copy'
import { data } from './home.data'
import { useHome } from './home'

const { t, os, href } = useHome()
const mascot = ref<InstanceType<typeof PerchedMascot>>()
</script>

<template>
  <section class="dc-close" aria-labelledby="close-title">
    <Backdrop />
    <div class="dc-wrap">
      <h2 id="close-title">{{ t.close.title }}</h2>
      <div class="dc-close__composer">
        <PerchedMascot ref="mascot" perch="close" :markup="data.mascots.close" :size="72" />
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
        <a class="dc-button" data-variant="primary" data-size="prominent" :href="href('/start/')">{{ t.start }}</a>
        <DownloadSplit drop="up" />
      </div>
    </div>
    <footer class="dc-foot">
      <span>{{ t.footer.copyright }}</span>
      <nav :aria-label="t.footer.label">
        <a v-for="link in projectLinks" :key="link.href" class="dc-button" data-variant="ghost" data-size="sm" :href="link.href">
          <Icon v-if="link.icon" :name="link.icon" :size="14" />
          {{ link.text }}
        </a>
      </nav>
    </footer>
  </section>
</template>
