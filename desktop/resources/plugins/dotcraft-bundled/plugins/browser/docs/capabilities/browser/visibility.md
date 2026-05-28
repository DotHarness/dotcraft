# Browser Capability: visibility

Browser visibility control. Use `set(true)` to present the browser visually to the user, `set(false)` to hide it, and `get()` to check whether it is currently visible. Keep browser work in the background unless the user asks to see it or live viewing is useful. When the browser should be visible, call `set(true)`.

```ts
const capability = await browser.capabilities.get("visibility");

interface VisibilityBrowserCapability {
  get(): Promise<boolean>;
  set(visible: boolean): Promise<void>;
}
```
