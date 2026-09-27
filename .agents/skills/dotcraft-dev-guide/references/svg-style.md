# DotCraft SVG Style

Apply these rules to SVG assets shipped with DotCraft built-in skills.

- Start from a simplified silhouette. For app-logo adaptations, remove tiny secondary parts before adding color.
- Avoid multiple outlines that touch or nearly touch at small sizes.
- Avoid decorative dots, micro-gradients, tiny labels, and nested icons inside icons.
- Prefer stroke widths that survive downscaling: about 1.8-2.5 in 24px viewBox, 2.5-4 in 48px/64px viewBox.
- Use rounded caps and joins for product UI icons unless the local style is sharper.
- Keep color count low. A reliable pattern is dark base, light main stroke/fill, one brand accent, and one small highlight.
- Check both dark and light backgrounds when the asset may appear in docs or settings surfaces.
- If text or symbols are needed, use geometric paths, not font text.
