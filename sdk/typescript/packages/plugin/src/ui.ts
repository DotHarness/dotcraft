import type {
  ButtonHTMLAttributes,
  ComponentType,
  CSSProperties,
  InputHTMLAttributes,
  ReactNode,
  TextareaHTMLAttributes,
} from "react";
import type { DesktopPluginSurfaceContext } from "./contracts.js";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger" | "accent" | "outline";
export type ButtonSize = "default" | "sm" | "icon" | "iconSm" | "prominent" | "toolbar";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  readonly variant?: ButtonVariant;
  readonly size?: ButtonSize;
  readonly iconLeft?: ReactNode;
  readonly loading?: boolean;
}

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "children"> {
  readonly icon: ReactNode;
  readonly label: string;
  readonly size?: number;
  readonly active?: boolean;
  readonly tone?: "neutral" | "danger";
  readonly bordered?: boolean;
  readonly tooltipLabel?: string;
}

export type FieldSize = "default" | "toolbar";

interface SharedFieldProps {
  readonly size?: FieldSize;
  readonly frameless?: boolean;
  readonly bare?: boolean;
  readonly invalid?: boolean;
  readonly mono?: boolean;
}

export interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "size">, SharedFieldProps {}

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement>, SharedFieldProps {}

export interface SelectOption<T extends string = string> {
  readonly value: T;
  readonly label: ReactNode;
  readonly description?: ReactNode;
  readonly icon?: ReactNode;
  readonly disabled?: boolean;
}

export interface SelectProps<T extends string = string> {
  readonly id?: string;
  readonly value: T;
  readonly options: readonly SelectOption<T>[];
  readonly onValueChange: (value: T) => void | boolean | Promise<void | boolean>;
  readonly ariaLabel?: string;
  readonly disabled?: boolean;
  readonly style?: CSSProperties;
  readonly appearance?: "field" | "frameless";
  readonly adaptiveWidth?: boolean;
}

export interface CheckboxProps {
  readonly id?: string;
  readonly checked: boolean;
  readonly onChange: (checked: boolean) => void;
  readonly disabled?: boolean;
  readonly label?: ReactNode;
  readonly ariaLabel?: string;
  readonly style?: CSSProperties;
}

export interface SpinnerProps {
  readonly size?: number;
  readonly label?: string;
}

export interface SkeletonProps {
  readonly width?: number | string;
  readonly height?: number | string;
  readonly radius?: number | string;
  readonly circle?: boolean;
  readonly style?: CSSProperties;
}

export interface ActionTooltipProps {
  readonly label: string;
  readonly multiline?: boolean;
  readonly children: ReactNode;
}

export interface ComboboxOption {
  readonly value: string;
  readonly label: string;
}

export interface ComboboxProps {
  readonly value: string;
  readonly options: readonly ComboboxOption[];
  readonly onValueChange: (value: string) => void;
  readonly ariaLabel?: string;
  readonly placeholder?: string;
  readonly disabled?: boolean;
}

export interface ModalHeaderProps {
  readonly icon: ReactNode;
  readonly title: string;
  readonly titleId?: string;
  readonly description?: ReactNode;
  readonly onClose?: () => void;
  readonly closeLabel?: string;
}

export interface PillSwitchProps {
  readonly checked: boolean;
  readonly onChange: (checked: boolean) => void;
  readonly disabled?: boolean;
  readonly "aria-label"?: string;
}

export interface SettingsPanelShellProps {
  readonly title: ReactNode;
  readonly description?: ReactNode;
  readonly action?: ReactNode;
  readonly breadcrumb?: ReactNode;
  readonly children: ReactNode;
}

export interface SettingsBreadcrumbProps {
  readonly parentLabel: string;
  readonly currentLabel: string;
  readonly onBack: () => void;
}

export interface SettingsGroupProps {
  readonly title?: string;
  readonly description?: ReactNode;
  readonly headerAction?: ReactNode;
  readonly children: ReactNode;
}

export interface SettingsRowProps {
  readonly label?: ReactNode;
  readonly description?: ReactNode;
  readonly control?: ReactNode;
  readonly controlMinWidth?: number | string;
  readonly children?: ReactNode;
}

export interface InlineDiffProps {
  readonly filePath: string;
  readonly line: number;
  readonly before: string;
  readonly after: string;
}

export interface PluginSurfaceProps<Surface extends string = string> {
  readonly name: Surface;
  readonly context: DesktopPluginSurfaceContext<Surface>;
  readonly children?: ReactNode;
}

export interface PluginSurfaceComponent {
  <Surface extends string = string>(props: PluginSurfaceProps<Surface>): ReactNode;
}

export interface DesktopSelectComponent {
  <T extends string = string>(props: SelectProps<T>): ReactNode;
}

export interface DesktopPluginUiComponents {
  readonly PluginSurface: PluginSurfaceComponent;
  readonly Button: ComponentType<ButtonProps>;
  readonly IconButton: ComponentType<IconButtonProps>;
  readonly Input: ComponentType<InputProps>;
  readonly Textarea: ComponentType<TextareaProps>;
  readonly Select: DesktopSelectComponent;
  readonly Checkbox: ComponentType<CheckboxProps>;
  readonly Spinner: ComponentType<SpinnerProps>;
  readonly Skeleton: ComponentType<SkeletonProps>;
  readonly ActionTooltip: ComponentType<ActionTooltipProps>;
  readonly Combobox: ComponentType<ComboboxProps>;
  readonly ModalHeader: ComponentType<ModalHeaderProps>;
  readonly PillSwitch: ComponentType<PillSwitchProps>;
  readonly SettingsPanelShell: ComponentType<SettingsPanelShellProps>;
  readonly SettingsBreadcrumb: ComponentType<SettingsBreadcrumbProps>;
  readonly SettingsGroup: ComponentType<SettingsGroupProps>;
  readonly SettingsRow: ComponentType<SettingsRowProps>;
  readonly InlineDiff: ComponentType<InlineDiffProps>;
}
