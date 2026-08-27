import {
  Button,
  type DesktopPluginActivate,
  type DesktopPluginToolRendererProps,
  type DesktopPluginViewProps,
} from "@dotcraft/plugin";
import "./index.css";

function ReviewView({ host }: DesktopPluginViewProps) {
  return (
    <main className="review-sample-view">
      <h1>Review sample</h1>
      <p>
        Ask the agent to call <code>review.normalize</code>. Its result uses this plugin&apos;s Desktop
        renderer.
      </p>
      <Button onClick={() => host.ui.showToast({ message: "Review sample is active." })}>
        Verify plugin
      </Button>
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

export const activate: DesktopPluginActivate = () => ({
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
});
