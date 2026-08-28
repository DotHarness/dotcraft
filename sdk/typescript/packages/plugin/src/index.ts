export type {
  DesktopLocalizedText,
  DesktopPluginActivate,
  DesktopPluginActivation,
  DesktopPluginAppBindings,
  DesktopPluginAppServer,
  DesktopPluginAppSurfaces,
  DesktopPluginAssistantMessageModel,
  DesktopPluginCommandContext,
  DesktopPluginCommandContribution,
  DesktopPluginConfirmOptions,
  DesktopPluginConversationViewContribution,
  DesktopPluginConversationViewProps,
  DesktopPluginDispose,
  DesktopPluginEnvironment,
  DesktopPluginEffectSetup,
  DesktopPluginEvents,
  DesktopPluginHost,
  DesktopPluginContributionIcon,
  DesktopPluginIconComponent,
  DesktopPluginIconProps,
  DesktopPluginLocalProject,
  DesktopPluginMetadata,
  DesktopPluginMainViewContribution,
  DesktopPluginNavigation,
  DesktopPluginOratorio,
  DesktopPluginOratorioBoardEvent,
  DesktopPluginOratorioContext,
  DesktopPluginOratorioEvent,
  DesktopPluginOratorioHandoffRequest,
  DesktopPluginOratorioRequest,
  DesktopPluginOratorioResponse,
  DesktopPluginSettingsPageContribution,
  DesktopPluginServices,
  DesktopPluginAppSurfaceContext,
  DesktopPluginComposerMascotSurfaceContext,
  DesktopPluginComposerSurfaceContext,
  DesktopPluginMascotActivity,
  DesktopPluginSurfaceComponent,
  DesktopPluginSurfaceContext,
  DesktopPluginSurfaceContextMap,
  DesktopPluginSurfaceProps,
  DesktopPluginSurfaceWrapper,
  DesktopPluginSurfaceWrapperProps,
  DesktopPluginMessageActionContribution,
  DesktopPluginToolPresentationModel,
  DesktopPluginToolRendererContribution,
  DesktopPluginToolRendererProps,
  DesktopPluginToastOptions,
  DesktopPluginUi,
  DesktopPluginViewProps,
  DesktopPluginWorkspaceReader,
} from "./contracts.js";
export type {
  ActionTooltipProps,
  ButtonProps,
  ButtonSize,
  ButtonVariant,
  CheckboxProps,
  ComboboxOption,
  ComboboxProps,
  DesktopPluginUiComponents,
  DesktopSelectComponent,
  FieldSize,
  IconButtonProps,
  InlineDiffProps,
  InputProps,
  ModalHeaderProps,
  PillSwitchProps,
  PluginSurfaceComponent,
  PluginSurfaceProps,
  SelectOption,
  SelectProps,
  SkeletonProps,
  SpinnerProps,
  SettingsBreadcrumbProps,
  SettingsGroupProps,
  SettingsPanelShellProps,
  SettingsRowProps,
  TextareaProps,
} from "./ui.js";

import { readDesktopPluginRuntime } from "./runtime.js";

const ui = readDesktopPluginRuntime().ui;

export const PluginSurface = ui.PluginSurface;
export const Button = ui.Button;
export const IconButton = ui.IconButton;
export const Input = ui.Input;
export const Textarea = ui.Textarea;
export const Select = ui.Select;
export const Checkbox = ui.Checkbox;
export const Spinner = ui.Spinner;
export const Skeleton = ui.Skeleton;
export const ActionTooltip = ui.ActionTooltip;
export const Combobox = ui.Combobox;
export const ModalHeader = ui.ModalHeader;
export const PillSwitch = ui.PillSwitch;
export const SettingsPanelShell = ui.SettingsPanelShell;
export const SettingsBreadcrumb = ui.SettingsBreadcrumb;
export const SettingsGroup = ui.SettingsGroup;
export const SettingsRow = ui.SettingsRow;
export const InlineDiff = ui.InlineDiff;
