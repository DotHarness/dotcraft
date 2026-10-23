export const settings = {
  schema: [],
  global: {},
  workspace: {},
  configPath: '',
  modelOptions: [],
  modelError: ''
};

export function ciGet(obj, key) {
  if (obj == null || typeof obj !== 'object') return undefined;
  if (Object.prototype.hasOwnProperty.call(obj, key)) return obj[key];
  const lower = key.toLowerCase();
  const match = Object.keys(obj).find(k => k.toLowerCase() === lower);
  return match === undefined ? undefined : obj[match];
}

export function isDynamicModelField(field) {
  return field.key === 'Model' || field.key === 'ConsolidationModel';
}

export function isObjectListSection(section) {
  return Boolean(section.rootKey && section.itemFields?.length);
}

export function schemaGetValue(data, section, fieldKey) {
  if (section.rootKey) return ciGet(data, section.rootKey);
  let obj = data;
  for (const segment of section.path ?? []) obj = ciGet(obj, segment);
  return ciGet(obj, fieldKey);
}

export function fieldId(sectionIdx, fieldKey) {
  return `sf-${sectionIdx}-${fieldKey.toLowerCase()}`;
}

export function itemFieldId(sectionIdx, itemIdx, fieldKey) {
  return `sf-${sectionIdx}-item-${itemIdx}-${fieldKey.toLowerCase()}`;
}

export function getObjectListArray(data, section) {
  const value = ciGet(data, section.rootKey);
  if (Array.isArray(value)) return value;
  if (!value || typeof value !== 'object') return [];
  return Object.entries(value).map(([name, item]) => ({ Name: name, ...(item && typeof item === 'object' && !Array.isArray(item) ? item : {}) }));
}

const GROUPS = { http: ['Url', 'Headers'], stdio: ['Command', 'Arguments', 'EnvironmentVariables'], subprocess: ['Command', 'Args', 'WorkingDirectory', 'Env'] };

export function fieldGroups(fieldKey) {
  return Object.entries(GROUPS).filter(([, keys]) => keys.includes(fieldKey)).map(([group]) => group);
}

export function isGroupVisible(groups, transport) {
  if (!groups.length) return true;
  return groups.includes(transport) || (transport === '' && !groups.includes('http'));
}

function cloneJson(value) {
  return value === undefined || value === null || typeof value !== 'object' ? value : JSON.parse(JSON.stringify(value));
}

export function createEmptyItem(section, name) {
  const item = { Name: name };
  for (const field of section.itemFields) {
    if (field.key === 'Name') continue;
    if (field.defaultValue != null) item[field.key] = cloneJson(field.defaultValue);
    else if (field.type === 'bool') item[field.key] = false;
    else if (field.key === 'Transport') item[field.key] = field.options?.[0] ?? '';
  }
  return item;
}

function serializeObjectList(items) {
  const result = {};
  for (const item of items) {
    const name = String(ciGet(item, 'Name') || '').trim();
    if (!name) continue;
    const copy = cloneJson(item);
    delete copy.Name;
    result[name] = copy;
  }
  return result;
}

function readKeyValueMap(el) {
  const map = {};
  el?.querySelectorAll('.dash-kv-editor__row').forEach(row => {
    const key = row.querySelector('[data-kv="key"]').value.trim();
    if (key) map[key] = row.querySelector('[data-kv="value"]').value;
  });
  return map;
}

function readField(field, el, rawJson, inList = false) {
  if (!el) return undefined;
  if (field.type === 'bool') {
    const value = el.dataset.value;
    return value === '' ? undefined : value === 'true';
  }
  if (field.type === 'keyValueMap') {
    const map = readKeyValueMap(el);
    return Object.keys(map).length || inList ? map : undefined;
  }
  const raw = el.value;
  if (field.type === 'json') {
    if (!raw.trim()) return undefined;
    return rawJson ? raw : JSON.parse(raw);
  }
  if (field.type === 'stringList') {
    const lines = raw.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
    return lines.length || inList ? lines : undefined;
  }
  if (raw === '') return undefined;
  return field.type === 'number' ? Number(raw) : raw;
}

function collectObjectList(section, sectionIdx, root, rawJson) {
  const items = [];
  root.querySelectorAll(`[data-object-list="${sectionIdx}"] .dash-object-item`).forEach(card => {
    const itemIdx = card.dataset.item;
    const item = {};
    for (const field of section.itemFields) {
      const value = readField(field, root.querySelector(`#${itemFieldId(sectionIdx, itemIdx, field.key)}`), rawJson, true);
      if (value !== undefined) item[field.key] = value;
    }
    items.push(item);
  });
  return serializeObjectList(items);
}

function setNested(result, section, fieldKey, value) {
  let obj = result;
  for (const segment of section.path ?? []) obj = obj[segment] ??= {};
  obj[fieldKey] = value;
}

function managedTopLevelKeys() {
  const keys = new Set();
  for (const section of settings.schema) {
    if (section.rootKey) keys.add(section.rootKey.toLowerCase());
    else if (section.path?.length) {
      if (section.fields.length) keys.add(section.path[0].toLowerCase());
    } else section.fields.forEach(field => keys.add(field.key.toLowerCase()));
  }
  return keys;
}

export function collectForm(root, { rawJson = false } = {}) {
  const result = {};
  settings.schema.forEach((section, sectionIdx) => {
    if (isObjectListSection(section)) {
      const items = collectObjectList(section, sectionIdx, root, rawJson);
      // Omitting a never-defined empty root keeps saves from clearing inherited user entries.
      if (Object.keys(items).length || ciGet(settings.workspace, section.rootKey) !== undefined) result[section.rootKey] = items;
      return;
    }
    for (const field of section.fields) {
      const id = fieldId(sectionIdx, field.key);
      let value;
      try {
        value = readField(field, root.querySelector(`#${id}`), rawJson);
      } catch (error) {
        error.fieldId = id;
        throw error;
      }
      if (value !== undefined) setNested(result, section, field.key, value);
    }
  });
  const managed = managedTopLevelKeys();
  for (const [key, value] of Object.entries(settings.workspace)) {
    if (!managed.has(key.toLowerCase()) && ciGet(result, key) === undefined) result[key] = cloneJson(value);
  }
  return result;
}
