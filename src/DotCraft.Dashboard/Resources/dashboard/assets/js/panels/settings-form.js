import { onAction } from '../core/actions.js';
import { formatCount, humanizeKey } from '../core/format.js';
import { attrs, button, chevron, esc } from '../core/html.js';
import { displayName, fieldControl } from './settings-fields.js';
import {
  ciGet,
  collectForm,
  createEmptyItem,
  fieldGroups,
  fieldId,
  getObjectListArray,
  isGroupVisible,
  isObjectListSection,
  itemFieldId,
  schemaGetValue,
  settings
} from './settings-model.js';

const BLOCK_TYPES = ['json', 'textarea', 'stringList', 'keyValueMap'];
const TRANSPORT_LABELS = { http: 'Remote (HTTP)', stdio: 'Local command', subprocess: 'Local process' };

function sourceText(workspaceValue, globalValue, field) {
  if (workspaceValue != null) return 'set in this workspace';
  if (globalValue != null) return 'inherited from user settings';
  return field.defaultValue != null ? 'using the default' : 'not set';
}

function itemNoun(section) {
  const words = humanizeKey(section.rootKey).split(' ');
  return words[words.length - 1].toLowerCase().replace(/s$/, '');
}

function group({ title, description, action = '', search, body }) {
  return `<section class="dc-settings-group" data-search="${esc(search)}">
    <header class="dc-settings-group__header"${attrs({ 'data-has-action': Boolean(action), 'data-described': Boolean(description) })}>
      <div class="dc-settings-group__heading"><h2 class="dc-settings-group__title">${esc(title)}</h2>${description ? `<div class="dc-settings-group__description">${esc(description)}</div>` : ''}</div>
      ${action ? `<div class="dc-settings-group__action">${action}</div>` : ''}
    </header>
    <div class="dc-settings-group__body">${body}</div>
  </section>`;
}

function fieldRow(section, sectionIdx, field) {
  const id = fieldId(sectionIdx, field.key);
  const jsonKey = [...(section.path ?? []), field.key].join('.');
  const value = schemaGetValue(settings.workspace, section, field.key);
  const inherited = schemaGetValue(settings.global, section, field.key);
  const block = BLOCK_TYPES.includes(field.type);
  const modelHint = field.key === 'Model' && settings.modelError ? `${settings.modelError} ` : '';
  const hint = `${field.hint ?? ''} ${modelHint}`.trim();
  const text = `<div${block ? '' : ' class="dc-settings-row__text"'}>
    <label class="dc-settings-row__label" for="${id}"><span class="dash-setting-label" data-tooltip="${esc(`${jsonKey}, ${sourceText(value, inherited, field)}`)}">${esc(displayName(field))}</span></label>
    ${hint ? `<div class="dc-settings-row__hint">${esc(hint)}</div>` : ''}
  </div>`;
  const control = fieldControl({ id, field, value, inherited });
  return `<div class="dc-settings-row" data-orientation="${block ? 'block' : 'inline'}" data-search-row="${esc(`${jsonKey} ${displayName(field)} ${field.hint ?? ''}`.toLowerCase())}">
    ${text}${block ? control : `<div class="dc-settings-row__control">${control}</div>`}
  </div>`;
}

function objectItem(section, sectionIdx, item, itemIdx) {
  const transportField = section.itemFields.find(field => field.key === 'Transport');
  const transport = String(ciGet(item, 'Transport') ?? transportField?.defaultValue ?? transportField?.options?.[0] ?? '').toLowerCase();
  const nameId = itemFieldId(sectionIdx, itemIdx, 'Name');
  const fields = section.itemFields.filter(field => field.key !== 'Name').map(field => {
    const id = itemFieldId(sectionIdx, itemIdx, field.key);
    return `<div class="dash-object-field"${attrs({ 'data-groups': fieldGroups(field.key).join(' ') || undefined, hidden: !isGroupVisible(fieldGroups(field.key), transport) })}>
      <label class="dash-object-field__label" for="${id}">${esc(displayName(field))}</label>
      ${fieldControl({ id, field, value: ciGet(item, field.key), required: true, change: field.key === 'Transport' ? 'transport-change' : undefined })}
    </div>`;
  }).join('');
  return `<div class="dc-settings-row" data-orientation="block" data-search-row="${esc(String(ciGet(item, 'Name') ?? '').toLowerCase())}">
    <div class="dash-object-item" data-item="${itemIdx}">
      <div class="dash-object-item__head">
        <input class="dc-field dash-object-item__name" data-mono aria-label="${esc(`${humanizeKey(itemNoun(section))} name`)}" id="${nameId}" value="${esc(ciGet(item, 'Name') ?? '')}">
        ${transportField ? `<span class="dash-secondary dash-object-item__transport">${esc(TRANSPORT_LABELS[transport] ?? transport)}</span>` : ''}
        <span class="dash-save-bar__spacer"></span>
        ${button('Remove', { variant: 'danger', size: 'sm', iconName: 'trash2', attributes: { 'data-action': 'remove-object-item' } })}
      </div>
      ${fields}
    </div>
  </div>`;
}

function objectListGroup(section, sectionIdx) {
  const items = getObjectListArray(settings.workspace, section);
  const inherited = getObjectListArray(settings.global, section).length;
  const noun = itemNoun(section);
  const description = !items.length && inherited ? `Using ${formatCount(inherited)} from user settings` : '';
  return group({
    title: section.section,
    description,
    search: `${section.section} ${noun}`.toLowerCase(),
    action: button(`Add ${noun}`, { size: 'sm', iconName: 'plus', attributes: { 'data-action': 'add-object-item', 'data-section': sectionIdx } }),
    body: `<div class="dash-object-list" data-object-list="${sectionIdx}" data-next="${items.length}">${items.map((item, index) => objectItem(section, sectionIdx, item, index)).join('')}</div>
      <div class="dc-settings-row" data-orientation="block">
        <button type="button" class="dash-text-action dash-text-action--disclosure" aria-expanded="false" data-action="toggle-raw-json" data-section="${sectionIdx}">Raw JSON${chevron(false)}</button>
      </div>`
  });
}

export function renderGroups() {
  return settings.schema.map((section, sectionIdx) => {
    if (isObjectListSection(section)) return objectListGroup(section, sectionIdx);
    return group({
      title: section.section,
      search: section.section.toLowerCase(),
      body: section.fields.map(field => fieldRow(section, sectionIdx, field)).join('')
    });
  }).join('');
}

function rawJson(toggle) {
  const sectionIdx = Number(toggle.dataset.section);
  const section = settings.schema[sectionIdx];
  const all = collectForm(toggle.closest('.dash-settings'), { rawJson: true });
  return JSON.stringify(all[section.rootKey] ?? {}, null, 2);
}

onAction('click', {
  'add-object-item': el => {
    const sectionIdx = Number(el.dataset.section);
    const section = settings.schema[sectionIdx];
    const list = el.closest('.dc-settings-group').querySelector('.dash-object-list');
    const names = new Set([...list.querySelectorAll('.dash-object-item__name')].map(input => input.value));
    let index = list.children.length + 1;
    while (names.has(`${itemNoun(section)}_${index}`)) index++;
    const itemIdx = Number(list.dataset.next);
    list.dataset.next = String(itemIdx + 1);
    list.insertAdjacentHTML('beforeend', objectItem(section, sectionIdx, createEmptyItem(section, `${itemNoun(section)}_${index}`), itemIdx));
    list.lastElementChild.querySelector('.dash-object-item__name').focus();
  },
  'remove-object-item': el => el.closest('.dc-settings-row').remove(),
  'toggle-raw-json': el => {
    const open = el.getAttribute('aria-expanded') !== 'true';
    el.setAttribute('aria-expanded', String(open));
    el.querySelector('.dc-disclosure-chevron').toggleAttribute('data-expanded', open);
    el.nextElementSibling?.remove();
    if (open) el.insertAdjacentHTML('afterend', `<pre class="dash-code">${esc(rawJson(el))}</pre>`);
  }
});
onAction('change', {
  'transport-change': el => {
    const item = el.closest('.dash-object-item');
    const transport = el.value.toLowerCase();
    item.querySelectorAll('.dash-object-field[data-groups]').forEach(field => {
      field.hidden = !isGroupVisible(field.dataset.groups.split(' '), transport);
    });
    const label = item.querySelector('.dash-object-item__transport');
    if (label) label.textContent = TRANSPORT_LABELS[transport] ?? transport;
  }
});
