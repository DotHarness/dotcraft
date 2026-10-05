<script setup lang="ts">
import type { ProductId } from './copy'
import { useHome } from './home'

const { t, href } = useHome()

const sticker: Record<ProductId, { tag: string; tilt: number }> = {
  desktop: { tag: '#2449e6', tilt: -1.4 },
  mobile: { tag: '#15803d', tilt: 1.4 },
  cli: { tag: '#1b1e27', tilt: 1.3 },
  satellite: { tag: '#13256e', tilt: -1.2 },
  oratorio: { tag: '#d24c0e', tilt: 1.4 },
  'chat-bots': { tag: '#c21d6b', tilt: -1.4 },
  harness: { tag: '#5527d0', tilt: 1.2 },
  sdks: { tag: '#0d766e', tilt: -1.5 },
  avatar: { tag: '#a76600', tilt: 1.3 }
}
</script>

<template>
  <section class="dc-section" aria-labelledby="products-title">
    <div class="dc-wrap">
      <header class="dc-head">
        <h2 id="products-title">{{ t.products.title }}</h2>
      </header>
      <div class="dc-products">
        <a
          v-for="row in t.products.rows"
          :key="row.label"
          class="dc-product"
          :href="href(row.href)"
          :style="{ '--tag': sticker[row.id].tag, '--tilt': `${sticker[row.id].tilt}deg` }"
        >
          <img class="dc-product__art" :src="row.art" alt="" width="540" height="470" loading="lazy" />
          <span class="dc-product__label">
            <span class="dc-product__head">
              <span class="dc-product__name">{{ row.label }}</span>
              <span class="dc-product__get">{{ row.get }}</span>
            </span>
            <span class="dc-product__hint">{{ row.hint }}</span>
          </span>
        </a>
      </div>
    </div>
  </section>
</template>
