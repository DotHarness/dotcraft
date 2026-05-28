#!/usr/bin/env node
import fs from 'node:fs/promises'
import path from 'node:path'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'

function parseArgs(argv) {
  const args = {
    out: 'references/svg-preview',
    title: 'SVG Preview',
    heroSize: 720,
    sizes: [16, 20, 32, 48, 64],
    files: []
  }

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i]
    if (arg === '--out') {
      args.out = argv[++i]
    } else if (arg === '--title') {
      args.title = argv[++i]
    } else if (arg === '--hero-size') {
      args.heroSize = Number(argv[++i])
    } else if (arg === '--sizes') {
      args.sizes = argv[++i].split(',').map((value) => Number(value.trim())).filter(Boolean)
    } else {
      args.files.push(arg)
    }
  }

  if (args.files.length === 0) {
    throw new Error('Provide at least one SVG file path.')
  }
  if (!Number.isFinite(args.heroSize) || args.heroSize < 64) {
    throw new Error('--hero-size must be a number greater than or equal to 64.')
  }
  return args
}

function escapeHtml(value) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function imageTag(src, size, className = '') {
  const classAttribute = className ? ` class="${className}"` : ''
  return `<img${classAttribute} src="${escapeHtml(src)}" style="width:${size}px;height:${size}px" alt="">`
}

function safeStem(file, index) {
  const stem = path.basename(file, path.extname(file))
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  return `${index + 1}-${stem || 'svg'}`
}

function buildHtml({ title, heroSize, sizes, files }) {
  const rows = files.map((file) => {
    const abs = path.resolve(file)
    const src = pathToFileURL(abs).href
    const name = path.basename(file)
    const previews = sizes
      .map((size) => `
          <div class="size-tile">
            <div class="size-box">${imageTag(src, size)}</div>
            <div class="size-label">${size}px</div>
          </div>`)
      .join('')
    return `
      <section class="asset">
        <div class="hero-pair">
          <div class="hero-surface hero-dark">${imageTag(src, heroSize, 'hero-img')}</div>
          <div class="hero-surface hero-light">${imageTag(src, heroSize, 'hero-img')}</div>
        </div>
        <div class="asset-footer">
          <div class="meta">
            <div class="name">${escapeHtml(name)}</div>
            <div class="path">${escapeHtml(abs)}</div>
          </div>
          <div class="size-ramp">${previews}</div>
        </div>
      </section>`
  }).join('')

  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <title>${escapeHtml(title)}</title>
    <style>
      :root {
        color-scheme: dark;
        font-family: Inter, "Segoe UI", sans-serif;
        background: #0b1020;
        color: #f8fafc;
        --hero-size: ${Math.round(heroSize)}px;
      }
      * { box-sizing: border-box; }
      body { margin: 0; padding: 32px; background: #0b1020; }
      .wrap { max-width: 1440px; margin: 0 auto; }
      h1 { margin: 0 0 24px; font-size: 20px; line-height: 1.25; }
      .asset {
        min-height: calc(var(--hero-size) + 164px);
        margin-bottom: 36px;
        padding: 28px;
        border: 1px solid #263149;
        border-radius: 14px;
        background: #111827;
      }
      .hero-pair {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 22px;
      }
      .hero-surface {
        min-height: calc(var(--hero-size) + 88px);
        display: grid;
        place-items: center;
        border-radius: 12px;
        overflow: hidden;
      }
      .hero-dark { background: #0b1020; border: 1px solid #263149; }
      .hero-light { background: #f8fafc; border: 1px solid #d7deeb; }
      .hero-img {
        max-width: calc(100% - 48px);
        max-height: calc(var(--hero-size) + 24px);
        object-fit: contain;
        image-rendering: auto;
      }
      .asset-footer {
        display: grid;
        grid-template-columns: minmax(260px, 1fr) auto;
        gap: 22px;
        align-items: center;
        margin-top: 18px;
      }
      .meta { min-width: 0; }
      .name { font-size: 14px; font-weight: 700; }
      .path {
        margin-top: 5px;
        max-width: 760px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        color: #a8b1c7;
        font-size: 11px;
      }
      .size-ramp {
        display: flex;
        align-items: end;
        gap: 12px;
        padding: 10px;
        border-radius: 10px;
        background: #0b1020;
        border: 1px solid #263149;
      }
      .size-tile { display: grid; gap: 6px; justify-items: center; }
      .size-box {
        width: 72px;
        height: 72px;
        display: grid;
        place-items: center;
        border-radius: 8px;
        background: #f8fafc;
      }
      .size-box img { object-fit: contain; image-rendering: auto; }
      .size-label { color: #a8b1c7; font-size: 10px; line-height: 1; }
      @media (max-width: 900px) {
        .hero-pair,
        .asset-footer { grid-template-columns: 1fr; }
        .size-ramp { justify-content: start; overflow-x: auto; }
      }
    </style>
  </head>
  <body>
    <main class="wrap">
      <h1>${escapeHtml(title)}</h1>
      ${rows}
    </main>
  </body>
</html>`
}

async function main() {
  const args = parseArgs(process.argv.slice(2))
  await fs.mkdir(args.out, { recursive: true })
  const htmlPath = path.join(args.out, 'preview.html')
  const pngPath = path.join(args.out, 'preview.png')
  await fs.writeFile(htmlPath, buildHtml(args), 'utf8')

  const requireFromCwd = createRequire(path.join(process.cwd(), 'package.json'))
  const { chromium } = requireFromCwd('playwright')
  const browser = await chromium.launch({ channel: 'msedge', headless: true })
  const page = await browser.newPage({
    viewport: {
      width: Math.max(1800, (args.heroSize * 2) + 320),
      height: Math.max(1080, args.heroSize + 360)
    },
    deviceScaleFactor: 1
  })
  await page.goto(pathToFileURL(path.resolve(htmlPath)).href)
  await page.screenshot({ path: pngPath, fullPage: true })

  const singlePngPaths = []
  for (const [index, file] of args.files.entries()) {
    const stem = safeStem(file, index)
    const singleHtmlPath = path.join(args.out, `preview-${stem}.html`)
    const singlePngPath = path.join(args.out, `preview-${stem}.png`)
    await fs.writeFile(
      singleHtmlPath,
      buildHtml({
        ...args,
        title: `${args.title} - ${path.basename(file)}`,
        files: [file]
      }),
      'utf8'
    )
    await page.goto(pathToFileURL(path.resolve(singleHtmlPath)).href)
    await page.screenshot({ path: singlePngPath, fullPage: true })
    singlePngPaths.push(singlePngPath)
  }

  await browser.close()

  console.log(`HTML: ${htmlPath}`)
  console.log(`PNG:  ${pngPath}`)
  for (const singlePngPath of singlePngPaths) {
    console.log(`PNG:  ${singlePngPath}`)
  }
  console.log(`Hero: ${args.heroSize}px`)
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error))
  process.exit(1)
})
