import type * as React from "react";
import type * as ReactDom from "react-dom";
import type * as JsxRuntime from "react/jsx-runtime";
import type { DesktopPluginUiComponents } from "./ui.js";

const runtimeKey = Symbol.for("dotcraft.desktop-plugin.runtime");

export interface DesktopPluginRuntime {
  readonly react: typeof React;
  readonly jsxRuntime: typeof JsxRuntime;
  readonly reactDom: Pick<typeof ReactDom, "createPortal">;
  readonly ui: DesktopPluginUiComponents;
}

export function installDesktopPluginRuntime(runtime: DesktopPluginRuntime): void {
  runtimeGlobal()[runtimeKey] = runtime;
}

export function readDesktopPluginRuntime(): DesktopPluginRuntime {
  const runtime = runtimeGlobal()[runtimeKey];
  if (!runtime) {
    throw new Error("DotCraft Desktop Plugin runtime is not installed.");
  }
  return runtime;
}

function runtimeGlobal(): typeof globalThis & Record<symbol, DesktopPluginRuntime | undefined> {
  return globalThis as typeof globalThis & Record<symbol, DesktopPluginRuntime | undefined>;
}
