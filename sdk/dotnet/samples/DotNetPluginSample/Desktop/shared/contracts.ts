export const REVIEW_APPEARANCE_SERVICE_ID = "acme.review-core.appearance";
export const REVIEW_FOCUS_EVENT = "acme.review-core.focus";
export const REVIEW_OVERLAY_SURFACE = "acme.review-core.app.overlay";

export type ReviewFocusSource = "app-overlay" | "composer-model" | "composer-toolbar" | "main-view";

export interface ReviewAppearanceService {
  readonly label: string;
  focus(source: ReviewFocusSource): void;
}

export interface ReviewFocusEvent {
  readonly source: ReviewFocusSource;
}

export interface ReviewOverlaySurfaceContext {
  readonly title: string;
}

declare module "@dotcraft/plugin" {
  interface DesktopPluginSurfaceContextMap {
    readonly "acme.review-core.app.overlay": ReviewOverlaySurfaceContext;
  }
}
