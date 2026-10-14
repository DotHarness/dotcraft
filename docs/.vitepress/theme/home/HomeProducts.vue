<script setup lang="ts">
import Icon from './Icon.vue'
import { useHome } from './home'
import desktopDark from './media/explore-desktop-dark.webp'
import desktopLight from './media/explore-desktop-light.webp'
import mobileDark from './media/explore-mobile-dark.webp'
import mobileLight from './media/explore-mobile-light.webp'

const { t, href } = useHome()

const shots: Record<string, { light: string; dark: string }> = {
  desktop: { light: desktopLight, dark: desktopDark },
  mobile: { light: mobileLight, dark: mobileDark }
}

const terminal = [
  { prompt: true, text: 'dotcraft exec "Why did the nightly build fail?"' },
  { text: 'SyncWorkerTests timed out. The worker retried in a tight loop' },
  { text: 'after a 429, so I added exponential backoff with jitter in' },
  { text: 'src/sync/worker.ts. The suite passes now.' },
  { prompt: true, text: '' }
]
</script>

<template>
  <section class="dc-section" aria-labelledby="products-title">
    <div class="dc-wrap">
      <header class="dc-head">
        <h2 id="products-title">{{ t.products.title }}</h2>
      </header>
      <div class="dc-products">
        <a v-for="row in t.products.rows" :key="row.id" class="dc-product" :href="href(row.href)">
          <span class="dc-product__stage dc-field" :data-product="row.id" aria-hidden="true">
            <template v-if="row.id === 'cli'">
              <span class="dc-product__terminal">
                <span class="dc-product__bar"><i /><i /><i /></span>
                <code>
                  <span v-for="(line, index) in terminal" :key="index" class="dc-product__line" :data-prompt="line.prompt || undefined">{{ line.text }}</span>
                </code>
              </span>
            </template>
            <template v-else>
              <img class="dc-product__shot" data-theme="light" :src="shots[row.id].light" alt="" loading="lazy" />
              <img class="dc-product__shot" data-theme="dark" :src="shots[row.id].dark" alt="" loading="lazy" />
            </template>
          </span>
          <span class="dc-product__name">{{ row.label }}</span>
          <span class="dc-product__hint">{{ row.hint }}</span>
          <span class="dc-product__get">
            {{ row.get }}
            <Icon name="arrow-right" :size="14" :stroke="2" />
          </span>
        </a>
      </div>
    </div>
  </section>
</template>
