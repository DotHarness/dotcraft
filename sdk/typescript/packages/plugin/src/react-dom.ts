import { readDesktopPluginRuntime } from "./runtime.js";

const runtime = readDesktopPluginRuntime().reactDom;

export const createPortal = runtime.createPortal;
