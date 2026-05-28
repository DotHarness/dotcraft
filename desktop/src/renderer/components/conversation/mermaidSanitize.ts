import DOMPurify from 'dompurify'

export function sanitizeMermaidSvg(svg: string, doc: Document = document): string {
  const sanitized = DOMPurify.sanitize(svg, {
    USE_PROFILES: { html: true, svg: true, svgFilters: true },
    ADD_TAGS: ['foreignObject'],
    ADD_ATTR: ['dominant-baseline'],
    HTML_INTEGRATION_POINTS: { foreignobject: true },
    FORBID_TAGS: ['script'],
    RETURN_TRUSTED_TYPE: false
  })
  const template = doc.createElement('template')
  template.innerHTML = typeof sanitized === 'string' ? sanitized : String(sanitized)

  for (const element of Array.from(template.content.querySelectorAll('*'))) {
    for (const attr of Array.from(element.attributes)) {
      const name = attr.name.toLowerCase()
      if (
        name.startsWith('on') ||
        name === 'target' ||
        name === 'download' ||
        (isHrefAttribute(name) && !isFragmentHref(attr.value))
      ) {
        element.removeAttribute(attr.name)
      }
    }
  }

  for (const anchor of Array.from(template.content.querySelectorAll('a'))) {
    const parent = anchor.parentNode
    if (!parent) continue
    while (anchor.firstChild) {
      parent.insertBefore(anchor.firstChild, anchor)
    }
    parent.removeChild(anchor)
  }

  return template.innerHTML
}

function isHrefAttribute(name: string): boolean {
  return name === 'href' || name === 'xlink:href'
}

function isFragmentHref(value: string): boolean {
  return value.trim().startsWith('#')
}
