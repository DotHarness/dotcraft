import { onAction } from '../core/actions.js';
import { attrs, button, esc, icon, iconButton, segmented } from '../core/html.js';
import { selectHtml } from '../core/menu.js';
import { isDynamicModelField, settings } from './settings-model.js';

export function displayName(field) {
  if (field.displayName) return field.displayName;
  return field.key.replace(/([a-z\d])([A-Z])/g, '$1 $2').replace(/^(\w)(.*)$/, (_, first, rest) => first + rest.toLowerCase());
}

function formatInherited(value, sensitive) {
  if (value === undefined || value === null || value === '') return '';
  if (sensitive) return 'set in user settings';
  if (typeof value === 'boolean') return value ? 'On' : 'Off';
  return typeof value === 'object' ? JSON.stringify(value) : String(value);
}

function placeholderFor(field, inherited) {
  const fromUser = formatInherited(inherited, field.sensitive);
  if (fromUser) return `Inherits ${fromUser}`;
  const fallback = formatInherited(field.defaultValue);
  return fallback ? `Default ${fallback}` : 'Not set';
}

function boolState(value) {
  if (value === true || String(value).toLowerCase() === 'true') return 'true';
  if (value === false || String(value).toLowerCase() === 'false') return 'false';
  return '';
}

function keyValueRow(key, value) {
  return `<div class="dash-kv-editor__row">
    <input class="dc-field" data-mono data-kv="key" placeholder="Name" aria-label="Variable name" value="${esc(key)}">
    <input class="dc-field" data-mono data-kv="value" placeholder="Value" aria-label="Variable value" value="${esc(value)}">
    ${iconButton('x', 'Remove row', { iconSize: 14, attributes: { 'data-action': 'remove-kv-row' } })}
  </div>`;
}

function selectOptions(field, value) {
  const dynamic = isDynamicModelField(field) && settings.modelOptions.length;
  const values = dynamic ? [...settings.modelOptions] : [...(field.options ?? [])];
  if (value && !values.includes(value)) values.push(value);
  if (dynamic) values.sort((a, b) => a.localeCompare(b));
  return values.map(option => ({ value: option, label: option }));
}

// `required` controls (object-list items) always hold a value, so they offer no Inherit choice.
export function fieldControl({ id, field, value, inherited, required = false, change }) {
  const placeholder = placeholderFor(field, inherited);
  const dynamicModel = isDynamicModelField(field) && settings.modelOptions.length;

  if (field.type === 'bool') {
    const current = boolState(value) || (required ? boolState(field.defaultValue) || 'true' : '');
    const inheritLabel = formatInherited(inherited) || formatInherited(field.defaultValue);
    return segmented(displayName(field), [
      ...(required ? [] : [{ value: '', label: inheritLabel ? `Inherit (${inheritLabel})` : 'Inherit' }]),
      { value: 'true', label: 'On' },
      { value: 'false', label: 'Off' }
    ], current, 'set-tristate', { attributes: { id, 'data-value': current } });
  }

  if (field.type === 'select' || dynamicModel) {
    const current = value == null ? (required ? String(field.defaultValue ?? field.options?.[0] ?? '') : '') : String(value);
    const options = selectOptions(field, current);
    return selectHtml({
      id,
      value: current,
      ariaLabel: displayName(field),
      width: 220,
      change,
      options: required ? options : [{ value: '', label: placeholder }, ...options]
    });
  }

  if (field.type === 'password') {
    return `<span class="dash-field-with-action">
      <input class="dc-field" type="password" autocomplete="off"${attrs({ id, value: value ?? '', placeholder })}>
      ${iconButton('eye', 'Show value', { attributes: { 'data-action': 'toggle-secret' } })}
    </span>`;
  }

  if (field.type === 'json' || field.type === 'textarea' || field.type === 'stringList') {
    const text = field.type === 'json'
      ? (typeof value === 'string' ? value : value == null ? '' : JSON.stringify(value, null, 2))
      : field.type === 'stringList'
        ? (Array.isArray(value) ? value.join('\n') : '')
        : (value ?? '');
    return `<div class="dash-field-block">
      <textarea class="dc-field" data-multiline spellcheck="false"${attrs({
        id,
        rows: field.type === 'json' ? 5 : 3,
        'data-mono': field.type !== 'textarea',
        'data-input': field.type === 'json' ? 'validate-json' : undefined,
        placeholder: field.type === 'stringList' ? `${placeholder}, one per line` : placeholder
      })}>${esc(text)}</textarea>
      ${field.type === 'json' ? `<div class="dash-field-error" role="alert" id="${id}-error" hidden></div>` : ''}
    </div>`;
  }

  if (field.type === 'keyValueMap') {
    const entries = value && typeof value === 'object' && !Array.isArray(value) ? Object.entries(value) : [];
    return `<div class="dash-kv-editor" id="${id}">
      ${(entries.length ? entries : [['', '']]).map(([key, entryValue]) => keyValueRow(key, entryValue)).join('')}
      <div>${button('Add variable', { variant: 'ghost', size: 'sm', iconName: 'plus', attributes: { 'data-action': 'add-kv-row' } })}</div>
    </div>`;
  }

  return `<input class="dc-field ${field.type === 'number' ? 'dash-input--number' : 'dash-input--text'}"${attrs({
    id,
    type: field.type === 'number' ? 'number' : 'text',
    min: field.min,
    max: field.max,
    value: value ?? '',
    placeholder
  })}>`;
}

export function validateJson(textarea) {
  const error = document.getElementById(`${textarea.id}-error`);
  let message = '';
  if (textarea.value.trim()) {
    try {
      JSON.parse(textarea.value);
    } catch (e) {
      message = `This isn’t valid JSON: ${e.message}`;
    }
  }
  error.textContent = message;
  error.hidden = !message;
  if (message) textarea.setAttribute('data-invalid', '');
  else textarea.removeAttribute('data-invalid');
  return !message;
}

onAction('click', {
  'set-tristate': el => {
    const group = el.closest('.dash-segmented');
    group.dataset.value = el.dataset.value;
    group.querySelectorAll('.dash-segmented__option').forEach(option => option.setAttribute('aria-pressed', String(option === el)));
  },
  'toggle-secret': el => {
    const input = el.previousElementSibling;
    const revealed = input.type === 'password';
    input.type = revealed ? 'text' : 'password';
    el.setAttribute('aria-label', revealed ? 'Hide value' : 'Show value');
    el.innerHTML = icon(revealed ? 'eyeOff' : 'eye', 15);
  },
  'add-kv-row': el => {
    el.closest('.dash-kv-editor').querySelector(':scope > div:last-child').insertAdjacentHTML('beforebegin', keyValueRow('', ''));
  },
  'remove-kv-row': el => el.closest('.dash-kv-editor__row').remove()
});
onAction('input', { 'validate-json': el => validateJson(el) });
