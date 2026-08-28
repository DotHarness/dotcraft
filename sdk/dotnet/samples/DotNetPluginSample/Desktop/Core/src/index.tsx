import {
  PluginSurface,
  type DesktopPluginActivate,
  type DesktopPluginSurfaceProps,
  type DesktopPluginSurfaceWrapperProps,
} from "@dotcraft/plugin";
import {
  REVIEW_APPEARANCE_SERVICE_ID,
  REVIEW_FOCUS_EVENT,
  REVIEW_OVERLAY_SURFACE,
  type ReviewAppearanceService,
  type ReviewFocusEvent,
} from "../../shared/contracts";
import "./index.css";

const overlayContext = { title: "Review Core overlay" } as const;

function ReviewBackground(_: DesktopPluginSurfaceProps<"app.background">) {
  return <div className="acme-review-background" aria-hidden="true" />;
}

function ReviewMascot({ context }: DesktopPluginSurfaceProps<"composer.mascot">) {
  return (
    <div
      className="acme-review-mascot"
      data-activity={context.activity}
      data-expression={context.expression}
      data-light={context.light}
      data-effort={context.reasoningEffort}
      data-speed={context.speed}
      data-context-max={context.contextMax ? "true" : "false"}
      data-reduced-motion={context.reducedMotion ? "true" : "false"}
      data-size={context.size}
      role="img"
      aria-label="Review companion"
    >
      <svg
        className="acme-review-mascot-character"
        viewBox="0 0 128 128"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M29 46 20 27l24 9M99 46l9-19-24 9"
          fill="#4b3ac7"
          stroke="#fff"
          strokeWidth="7"
          strokeLinejoin="round"
        />
        <rect x="22" y="30" width="84" height="76" rx="30" fill="#7357ff" stroke="#fff" strokeWidth="7" />
        <path d="M64 33c23 0 39 15 39 37S87 103 64 103V33Z" fill="#35c6df" opacity=".72" />
        <rect x="34" y="46" width="60" height="42" rx="17" fill="#f8fafc" />
        <circle cx="49" cy="64" r="6" fill="#283354" />
        <circle cx="79" cy="64" r="6" fill="#283354" />
        <path d="m53 76 8 7 15-17" stroke="#25a775" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" />
        <path d="M45 106v8M83 106v8" stroke="#fff" strokeWidth="8" strokeLinecap="round" />
      </svg>
      {context.submitRevision > 0 && (
        <span
          key={context.submitRevision}
          className="acme-review-mascot-submit-cue"
          aria-hidden="true"
        />
      )}
    </div>
  );
}

function ReviewAppFrame({ children }: DesktopPluginSurfaceWrapperProps<"app">) {
  return (
    <div className="acme-review-app-frame">
      {children}
      <PluginSurface name={REVIEW_OVERLAY_SURFACE} context={overlayContext} />
    </div>
  );
}

export const activate: DesktopPluginActivate = (host) => {
  let pulseTimer: number | undefined;

  host.effect(() => {
    document.documentElement.dataset.acmeReviewSkin = "active";
    return () => {
      if (pulseTimer !== undefined) window.clearTimeout(pulseTimer);
      delete document.documentElement.dataset.acmeReviewSkin;
      delete document.documentElement.dataset.acmeReviewPulse;
    };
  });

  host.events.on<ReviewFocusEvent>(REVIEW_FOCUS_EVENT, ({ source }) => {
    if (pulseTimer !== undefined) window.clearTimeout(pulseTimer);
    document.documentElement.dataset.acmeReviewPulse = source;
    pulseTimer = window.setTimeout(() => {
      delete document.documentElement.dataset.acmeReviewPulse;
      pulseTimer = undefined;
    }, 900);
  });

  const appearanceService: ReviewAppearanceService = {
    label: "Review Core skin",
    focus(source) {
      host.events.emit<ReviewFocusEvent>(REVIEW_FOCUS_EVENT, { source });
    },
  };
  host.services.provide(REVIEW_APPEARANCE_SERVICE_ID, appearanceService);

  host.ui.replace("app.background", ReviewBackground);
  host.ui.replace("composer.mascot", ReviewMascot);
  host.ui.wrap("app", ReviewAppFrame);
};
