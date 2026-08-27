import { readDesktopPluginRuntime } from "./runtime.js";

const runtime = readDesktopPluginRuntime().jsxRuntime;

export const Fragment = runtime.Fragment;
export const jsx = runtime.jsx;
export const jsxs = runtime.jsxs;
