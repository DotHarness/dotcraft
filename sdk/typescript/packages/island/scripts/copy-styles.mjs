import { readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'

const inline = (file) =>
  readFileSync(file, 'utf8').replace(/@import\s+['"](.+?)['"];[^\S\n]*\n?/g, (_, target) => inline(join(dirname(file), target)))

writeFileSync('dist/styles.css', inline('src/styles.css'))
