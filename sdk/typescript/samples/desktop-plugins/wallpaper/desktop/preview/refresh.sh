#!/bin/sh
set -e
cd "$(dirname "$0")/.."
npx dotcraft-plugin build >/dev/null
cp dist/index.css preview/index.css
rm -rf preview/assets && cp -r dist/assets preview/assets
node ../../../../node_modules/esbuild/bin/esbuild preview/preview.tsx --bundle --outfile=preview/preview.js --jsx=automatic --format=esm --loader:.svg=file --asset-names="assets/[name]-[hash]" --define:process.env.NODE_ENV='"development"' --log-level=warning
echo "preview refreshed"
