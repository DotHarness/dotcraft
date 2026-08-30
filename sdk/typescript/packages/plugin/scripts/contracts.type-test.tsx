import {
  PluginSurface,
  SegmentedControl,
  SettingsGroup,
  SettingsRow,
  Slider,
  type DesktopPluginActivate,
  type DesktopPluginHost,
  type DesktopPluginLocale,
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

host.appearance.setThemeSeedOverride({
  light: { surface: '#ffffff', ink: '#1a1c1f', accent: '#4f6cce', contrast: 45 },
  dark: { surface: '#141515', accent: '#4566cc' }
});
host.appearance.setBackdropPresentation({ surfaceOpacity: 0.72 });
host.appearance.setThemeSeedOverride(null);
host.appearance.setBackdropPresentation(null);

void host.ui.pickColor({
  title: "Choose accent",
  initialColor: "#4566cc",
  allowReset: true,
  defaultColor: "#4566cc",
}).then((result) => {
  if (result.kind === "select") {
    const selected: string = result.color;
    void selected;
  }
});

void host.ui.pickColor({ title: "Choose label color", initialColor: "#abc" });

// @ts-expect-error Resettable requests require a default color.
void host.ui.pickColor({ title: "Choose accent", initialColor: "#4566cc", allowReset: true });

// @ts-expect-error Non-resettable requests cannot provide a default color.
void host.ui.pickColor({ title: "Choose accent", initialColor: "#4566cc", defaultColor: "#4566cc" });

host.ui.add("composer", ({ host: componentHost, context }) => {
  componentHost.events.emit("sample.composer-mounted", context.threadId);
  return context.minimalChrome ? null : context.variant;
});

host.ui.replace("app.background", ({ context }) => context.rootElement.dataset.theme ?? null);
host.ui.add("app.status", ({ context }) => context.rootElement.dataset.theme ?? null);
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

const disposeEnvironment = host.environment.onChange(({ locale, theme }) => {
  const applied: "light" | "dark" = theme;
  const appLocale: DesktopPluginLocale = locale;
  host.events.emit("sample.environment", `${appLocale}:${applied}`);
});
disposeEnvironment();

// @ts-expect-error The environment reports an app locale, never a browser tag.
const browserTag: "zh-CN" = host.environment.locale;
void browserTag;

const disposeSession = host.session.onChange(({ workspacePath, threadId, mode, busy }) => {
  const foreground: string | null = workspacePath;
  host.events.emit("sample.session", `${foreground}:${threadId}:${mode}:${busy}`);
});
disposeSession();

// The foreground workspace is a Host reader; the thread's own workspace stays on the surface context.
const foregroundWorkspace: string | null = host.session.workspacePath;
void foregroundWorkspace;

const disposeSettings = host.settings.onChange<{ accent: string }>(({ value, writableScopes }) => {
  const accent: string = value.accent;
  host.events.emit("sample.settings", `${accent}:${writableScopes.join()}`);
});
disposeSettings();

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

void (
  <SegmentedControl
    value="system"
    options={[
      { value: "system", label: "System" },
      { value: "dark", label: "Dark" },
    ]}
    onValueChange={(mode) => {
      const chosen: "system" | "dark" = mode;
      void chosen;
    }}
    ariaLabel="Theme"
  />
);

void (
  <SettingsGroup title="Preview" flush>
    <SettingsRow label="Opacity" orientation="block" align="flex-start" htmlFor="opacity">
      <Slider
        value={72}
        min={30}
        max={100}
        ariaLabel="Opacity"
        valueText="72%"
        onValueChange={(value) => void value}
        onValueCommit={(value) => void value}
      />
    </SettingsRow>
  </SettingsGroup>
);

void (
  <SegmentedControl
    value="system"
    // @ts-expect-error A segment outside the chosen value union is not selectable.
    options={[{ value: "bright", label: "Bright" }]}
    onValueChange={(mode: "system") => void mode}
    ariaLabel="Theme"
  />
);

// @ts-expect-error Known surfaces require their declared context.
void <PluginSurface name="composer" context={{ threadId: "thread-1" }} />;

// @ts-expect-error Augmented surfaces retain their declared context.
void <PluginSurface name="sample.details" context={{ value: "wrong" }} />;

// @ts-expect-error Known mascot surfaces require their complete state context.
void <PluginSurface name="composer.mascot" context={composerContext} />;
