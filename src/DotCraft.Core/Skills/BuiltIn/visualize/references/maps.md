# Maps

- Let the map dominate the composition. Use at most one compact selection/detail area and only requested controls.
- Always project published GeoJSON/TopoJSON and sourced longitude/latitude with `d3-geo`; never hard-code or hand-draw geographic outlines. Use schematic maps only when asked.
- For world countries, import `https://esm.sh/@d3-maps/atlas@1.0.0/world/countries/countries-110m` and convert it with `topojson-client@3.1.0` using `feature(world, world.objects.features).features`. Join input ISO3 directly to `feature.properties.id`, which is already ISO3; do not convert it to numbers.
- For US states or counties, import `https://cdn.jsdelivr.net/npm/us-atlas@3/counties-10m.json/+esm`. For ZIP/ZCTA or city boundaries, obtain official Census or local open-data GeoJSON and inline it; do not guess sibling atlas paths or import raw JSON as JavaScript.
- Keep maps geographically legible: for local points, use published neighborhood, street, or comparable geometry; a blank field or lone administrative outline is not a basemap. Show the full city or region behind points or partial choropleths, and frame the locations with modest padding.
- Include the verified geometry in the final HTML. Before replying, check for blank basemaps, failed imports, missing labels, and unprojected points.
