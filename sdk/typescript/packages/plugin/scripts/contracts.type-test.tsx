import {
  PluginSurface,
  type DesktopPluginActivate,
  type DesktopPluginHost,
  type DesktopPluginSurfaceContext,
} from "../src/index.js";

declare module "../src/index.js" {
  interface DesktopPluginSurfaceContextMap {
    readonly "sample.details": {
      readonly value: number;
    };
  }
}

declare const host: DesktopPluginHost;

host.ui.add("composer", ({ host: componentHost, context }) => {
  componentHost.events.emit("sample.composer-mounted", context.threadId);
  return context.minimalChrome ? null : context.variant;
});

host.ui.replace("app.background", ({ context }) => context.rootElement.dataset.theme ?? null);
host.ui.replace("composer.mascot", ({ context }) => (
  `${context.activity}:${context.expression}:${context.submitRevision}`
));
host.ui.wrap("composer.toolbar.model", ({ context, children }) => context.busy ? null : children);
host.ui.add("sample.details", ({ context }) => context.value);
host.ui.add("sample.unknown", ({ context }) => {
  const unknownContext: unknown = context;
  return unknownContext == null ? null : String(unknownContext);
});

host.effect(() => host.events.on<string>("sample.message", (message) => {
  host.services.use<{ write(value: string): void }>("sample.writer")?.write(message);
}));

const disposeService = host.services.provide("sample.writer", {
  write(_value: string) {},
});
disposeService();

const activateWithDirectRegistration: DesktopPluginActivate = (pluginHost) => {
  pluginHost.ui.add("composer.after", () => null);
};

const activateAsync: DesktopPluginActivate = async () => {};
void activateWithDirectRegistration;
void activateAsync;

const composerContext: DesktopPluginSurfaceContext<"composer"> = {
  workspacePath: null,
  threadId: "thread-1",
  mode: "agent",
  busy: false,
  awaitingApproval: false,
  variant: "default",
  minimalChrome: false,
};

const welcomeComposerContext: DesktopPluginSurfaceContext<"composer"> = {
  ...composerContext,
  threadId: null,
};

const mascotContext: DesktopPluginSurfaceContext<"composer.mascot"> = {
  ...composerContext,
  size: 58,
  activity: "working",
  expression: "operator",
  light: "default",
  submitRevision: 1,
  reasoningEffort: "high",
  speed: "fast",
  contextMax: true,
  reducedMotion: false,
};

void <PluginSurface name="composer" context={composerContext}>content</PluginSurface>;
void <PluginSurface name="composer" context={welcomeComposerContext}>welcome</PluginSurface>;
void <PluginSurface name="composer.mascot" context={mascotContext}>mascot</PluginSurface>;
void <PluginSurface name="sample.details" context={{ value: 1 }} />;
void <PluginSurface name="sample.unknown" context={{ anything: true }} />;

// @ts-expect-error Known surfaces require their declared context.
void <PluginSurface name="composer" context={{ threadId: "thread-1" }} />;

// @ts-expect-error Augmented surfaces retain their declared context.
void <PluginSurface name="sample.details" context={{ value: "wrong" }} />;

// @ts-expect-error Known mascot surfaces require their complete state context.
void <PluginSurface name="composer.mascot" context={composerContext} />;
