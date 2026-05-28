/// <reference types="react" />
/// <reference types="react/jsx-runtime" />

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
