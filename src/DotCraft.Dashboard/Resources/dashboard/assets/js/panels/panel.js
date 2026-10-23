export function renderPanel(element, html) {
  const scrollTop = element.querySelector('.dash-panel-body')?.scrollTop ?? 0;
  element.innerHTML = html;
  const body = element.querySelector('.dash-panel-body');
  if (body) body.scrollTop = scrollTop;
}
