<script setup lang="ts">
import Icon from './Icon.vue'
import { data } from './home.data'
import { useHome } from './home'

const { t, href } = useHome()
</script>

<template>
  <section class="dc-section" aria-labelledby="products-title">
    <div class="dc-wrap">
      <header class="dc-head">
        <h2 id="products-title">{{ t.products.title }}</h2>
      </header>
      <div class="dc-products">
        <div class="dc-loop">
          <img :src="t.products.loop.src" :alt="t.products.loop.alt" width="1600" height="800" loading="lazy" />
        </div>
        <div class="dc-card">
          <a v-for="row in t.products.rows" :key="row.look" class="dc-row" :href="href(row.href)">
            <span class="dc-look" aria-hidden="true" v-html="data.looks[row.look]" />
            <span class="dc-row__copy">
              <span class="dc-row__label">{{ row.label }}</span>
              <span class="dc-row__hint">{{ row.hint }}</span>
            </span>
            <span class="dc-row__go"><Icon name="chevron-right" :stroke="2" /></span>
          </a>
        </div>
      </div>
    </div>
  </section>
</template>
