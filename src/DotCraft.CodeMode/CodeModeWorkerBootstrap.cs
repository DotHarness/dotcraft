namespace DotCraft.CodeMode;

internal static class CodeModeWorkerBootstrap
{
    public const string Source = """
        (() => {
          const host = {
            call: globalThis.__call,
            text: globalThis.__text,
            image: globalThis.__image,
            exit: globalThis.__exit,
            storeSet: globalThis.__storeSet,
            storeDelete: globalThis.__storeDelete,
            load: globalThis.__load
          };
          const catalog = JSON.parse(globalThis.__toolsJson);
          const aliases = JSON.parse(globalThis.__aliasesJson);
          for (const name of ['__call', '__text', '__image', '__exit', '__storeSet', '__storeDelete', '__load', '__toolsJson', '__aliasesJson'])
            delete globalThis[name];

          const define = (name, value) =>
            Object.defineProperty(globalThis, name, { value, writable: false, enumerable: false, configurable: false });

          const makeTool = name => async args => {
            const envelope = JSON.parse(await host.call(name, args));
            if (!envelope.ok) throw new Error(envelope.error);
            return envelope.value;
          };

          const functions = Object.create(null);
          for (const entry of catalog) functions[entry.name] = makeTool(entry.name);
          for (const alias of Object.keys(aliases))
            if (!(alias in functions)) functions[alias] = functions[aliases[alias]];
          Object.freeze(functions);
          const names = catalog.map(entry => entry.name);

          const distance = (a, b) => {
            const row = Array.from({ length: b.length + 1 }, (_, i) => i);
            for (let i = 1; i <= a.length; i++) {
              let previous = row[0];
              row[0] = i;
              for (let j = 1; j <= b.length; j++) {
                const current = row[j];
                row[j] = Math.min(row[j] + 1, row[j - 1] + 1, previous + (a[i - 1] === b[j - 1] ? 0 : 1));
                previous = current;
              }
            }
            return row[b.length];
          };
          const closeMatches = requested => {
            const needle = requested.toLowerCase();
            return names
              .map(name => {
                const candidate = name.toLowerCase();
                const score = candidate.includes(needle) || needle.includes(candidate) ? 0 : distance(needle, candidate);
                return { name, score };
              })
              .filter(entry => entry.score <= Math.max(2, Math.floor(needle.length / 3)))
              .sort((a, b) => a.score - b.score || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0))
              .slice(0, 3)
              .map(entry => entry.name);
          };

          const tools = new Proxy(functions, {
            get(target, property) {
              if (typeof property === 'symbol' || property === 'then') return undefined;
              if (Object.prototype.hasOwnProperty.call(target, property)) return target[property];
              const matches = closeMatches(property);
              const hint = matches.length > 0 ? ` Did you mean ${matches.map(name => `tools.${name}`).join(', ')}?` : '';
              throw new TypeError(`tools.${property} is not a callable tool.${hint} Filter ALL_TOOLS to find available tools.`);
            },
            set() { return false; },
            defineProperty() { return false; },
            deleteProperty() { return false; }
          });

          define('tools', tools);
          define('ALL_TOOLS', Object.freeze(catalog.map(entry => Object.freeze({ name: entry.name, description: entry.description }))));
          define('text', value => {
            let rendered = typeof value === 'string' ? value : JSON.stringify(value);
            if (rendered === undefined) rendered = String(value);
            const error = host.text(rendered);
            if (error) throw new RangeError(error);
          });
          define('image', value => {
            let mimeType;
            let data;
            if (typeof value === 'string') {
              const match = /^data:([^;,]+);base64,([\s\S]*)$/.exec(value);
              if (!match) {
                if (/^[A-Za-z][A-Za-z0-9+.-]*:/.test(value) && !value.startsWith('data:'))
                  throw new TypeError('image() does not accept remote URLs; pass a base64 data: URL or an MCP image content block.');
                throw new TypeError('image() expects a base64 data: URL or an MCP image content block.');
              }
              mimeType = match[1];
              data = match[2];
            } else if (value && typeof value === 'object' && value.type === 'image'
                       && typeof value.data === 'string' && typeof value.mimeType === 'string') {
              mimeType = value.mimeType;
              data = value.data;
            } else {
              throw new TypeError('image() expects a base64 data: URL or an MCP image content block.');
            }
            if (!/^image\/[A-Za-z0-9.+-]+$/.test(mimeType)) throw new TypeError(`image() does not accept media type '${mimeType}'.`);
            const error = host.image(mimeType, data);
            if (error) throw new TypeError(error);
          });
          define('exit', () => { host.exit(); });
          define('store', (key, value) => {
            if (typeof key !== 'string') throw new TypeError('store() requires a string key.');
            if (value === undefined) { host.storeDelete(key); return; }
            const json = JSON.stringify(value);
            if (json === undefined) throw new TypeError('store() requires a JSON-serializable value.');
            const error = host.storeSet(key, json);
            if (error) throw new RangeError(error);
          });
          define('load', key => {
            if (typeof key !== 'string') throw new TypeError('load() requires a string key.');
            const json = host.load(key);
            return json === null || json === undefined ? undefined : JSON.parse(json);
          });
          for (const name of ['eval', 'Function', 'WebAssembly', 'setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'queueMicrotask', 'require', 'console'])
            define(name, undefined);
        })();
        """;
}
