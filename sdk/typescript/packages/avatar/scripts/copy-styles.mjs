import { copyFileSync, cpSync } from 'node:fs'
for (const file of ['styles.css','rig.css','avatar.css','accessories.css']) copyFileSync('src/'+file,'dist/'+file)

cpSync('src/composer/styles', 'dist/composer/styles', { recursive: true })
