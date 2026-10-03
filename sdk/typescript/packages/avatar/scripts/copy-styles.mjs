import { copyFileSync, cpSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'

const inline = (file) =>
  readFileSync(file, 'utf8').replace(/@import\s+['"](.+?)['"];[^\S\n]*\n?/g, (_, target) => inline(join(dirname(file), target)))

for (const file of ['rig.css','avatar.css','accessories.css','effects.css']) copyFileSync('src/'+file,'dist/'+file)
writeFileSync('dist/styles.css', inline('src/styles.css'))

cpSync('src/composer/styles', 'dist/composer/styles', { recursive: true })
