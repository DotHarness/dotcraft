const ATTRIBUTES = { click: 'action', input: 'input', change: 'change' };
const handlers = { click: new Map(), input: new Map(), change: new Map() };

export function onAction(type, actions) {
  for (const [name, handler] of Object.entries(actions)) handlers[type].set(name, handler);
}

// Only the innermost element carrying the attribute handles an event, so nested actions never fire twice.
export function installActions() {
  for (const [type, attribute] of Object.entries(ATTRIBUTES)) {
    document.addEventListener(type, event => {
      const el = event.target.closest?.(`[data-${attribute}]`);
      const handler = el && handlers[type].get(el.dataset[attribute]);
      if (handler && !el.matches?.('[aria-disabled="true"]')) handler(el, event);
    });
  }
}
