import {
  Button,
  type DesktopPluginActivate,
  type DesktopPluginHost,
  type DesktopPluginSurfaceProps,
  type DesktopPluginSurfaceWrapperProps,
  type DesktopPluginToolRendererProps,
  type DesktopPluginViewProps,
} from "@dotcraft/plugin";
import {
  REVIEW_APPEARANCE_SERVICE_ID,
  REVIEW_OVERLAY_SURFACE,
  type ReviewAppearanceService,
  type ReviewFocusSource,
} from "../../shared/contracts";
import "./index.css";

function focusReviewSkin(host: DesktopPluginHost, source: ReviewFocusSource) {
  const service = host.services.use<ReviewAppearanceService>(REVIEW_APPEARANCE_SERVICE_ID);
  if (!service) {
    host.ui.showToast({ message: "Review Core is not active.", tone: "warning" });
    return;
  }
  service.focus(source);
}

function ReviewView({ host }: DesktopPluginViewProps) {
  return (
    <main className="review-sample-view">
      <h1>Review sample</h1>
      <p>
        Ask the agent to call <code>review.normalize</code>. Its result uses this plugin&apos;s Desktop
        renderer.
      </p>
      <p>The companion Review Core Desktop module owns the background and shared overlay surface.</p>
      <Button onClick={() => focusReviewSkin(host, "main-view")}>Pulse review background</Button>
    </main>
  );
}

function NormalizeResult({ presentation }: DesktopPluginToolRendererProps) {
  return (
    <section className="review-normalize-result" data-status={presentation.status}>
      <strong>Normalized review</strong>
      <p>{presentation.result ?? "Normalizing…"}</p>
    </section>
  );
}

function ReviewComposerFrame({ children, context }: DesktopPluginSurfaceWrapperProps<"composer">) {
  return (
    <div className="acme-review-composer-frame" data-mode={context.mode}>
      {children}
    </div>
  );
}

function ReviewComposerNotice({ context }: DesktopPluginSurfaceProps<"composer.before">) {
  if (context.minimalChrome) return null;
  return (
    <div className="acme-review-composer-notice" data-busy={context.busy}>
      Review workspace · {context.mode === "plan" ? "Plan" : "Agent"} mode
    </div>
  );
}

function ReviewComposerToolbarAction({ host, context }: DesktopPluginSurfaceProps<"composer.toolbar.leading">) {
  if (context.minimalChrome) return null;
  return (
    <Button
      variant="ghost"
      size="sm"
      onClick={() => focusReviewSkin(host, "composer-toolbar")}
    >
      Pulse background
    </Button>
  );
}

function ReviewModelPrefix({ host, context, children }: DesktopPluginSurfaceWrapperProps<"composer.toolbar.model">) {
  if (context.minimalChrome) return children;
  return (
    <>
      <Button
        variant="ghost"
        size="sm"
        onClick={() => focusReviewSkin(host, "composer-model")}
      >
        Review model
      </Button>
      {children}
    </>
  );
}

function ReviewSubscriptionStatus({ context }: DesktopPluginSurfaceProps<"composer.status.subscription">) {
  if (context.minimalChrome) return null;
  return <span className="acme-review-subscription-status">Review ready</span>;
}

function ReviewOverlay({ host, context }: DesktopPluginSurfaceProps<typeof REVIEW_OVERLAY_SURFACE>) {
  return (
    <div className="acme-review-overlay">
      <span>{context.title}</span>
      <Button
        variant="secondary"
        size="sm"
        onClick={() => focusReviewSkin(host, "app-overlay")}
      >
        Pulse
      </Button>
    </div>
  );
}

export const activate: DesktopPluginActivate = (host) => {
  host.ui.wrap("composer", ReviewComposerFrame);
  host.ui.add("composer.before", ReviewComposerNotice);
  host.ui.add("composer.toolbar.leading", ReviewComposerToolbarAction);
  host.ui.wrap("composer.toolbar.model", ReviewModelPrefix);
  host.ui.add("composer.status.subscription", ReviewSubscriptionStatus);
  host.ui.add(REVIEW_OVERLAY_SURFACE, ReviewOverlay);

  return {
    mainViews: [
      {
        id: "review",
        label: { default: "Review sample" },
        order: 80,
        component: ReviewView,
      },
    ],
    toolRenderers: [
      {
        id: "normalize-result",
        presentationId: "acme.review-normalize",
        component: NormalizeResult,
      },
    ],
  };
};
