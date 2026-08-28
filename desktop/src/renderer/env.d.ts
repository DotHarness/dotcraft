/// <reference types="react" />
/// <reference types="react/jsx-runtime" />
// Declares Vite's asset and CSS-module imports (`*.module.css`, `*.svg`, `?url`).
// This belongs to the renderer environment; it used to hang off a triple-slash
// comment in utils/theme.ts, so deleting that file's stylesheet imports would
// silently have taken every asset module's types with it.
/// <reference types="vite/client" />

declare const __APP_VERSION__: string

// Make React.JSX.Element available as JSX.Element globally
declare namespace JSX {
  type Element = React.JSX.Element
  type IntrinsicElements = React.JSX.IntrinsicElements
  type ElementClass = React.Component
  type ElementChildrenAttribute = React.JSX.ElementChildrenAttribute
  type LibraryManagedAttributes<C, P> = React.JSX.LibraryManagedAttributes<C, P>
  type IntrinsicAttributes = React.JSX.IntrinsicAttributes
  type IntrinsicClassAttributes<T> = React.JSX.IntrinsicClassAttributes<T>
}
