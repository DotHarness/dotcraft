export type * from "./satellite.js";
export type {
  DesktopPluginRunOnContext,
  DesktopLocalizedText,
  DesktopPluginActivate,
  DesktopPluginActivation,
  DesktopPluginAddOptions,
  DesktopPluginAppServer,
  DesktopPluginAppearance,
  DesktopPluginBackdropPresentation,
  DesktopPluginAssistantMessageModel,
  DesktopPluginCommandContext,
  DesktopPluginCommandContribution,
  DesktopPluginColorPickerBaseOptions,
  DesktopPluginColorPickerOptions,
  DesktopPluginColorPickerResult,
  DesktopPluginConfirmOptions,
  DesktopPluginConversationAsideContext,
  DesktopPluginConversationAsideLayout,
  DesktopPluginConversationViewContribution,
  DesktopPluginConversationViewProps,
  DesktopPluginDetailPanelTab,
  DesktopPluginDispose,
  DesktopPluginEnvironment,
  DesktopPluginEnvironmentSnapshot,
  DesktopPluginFolderPickerOptions,
  DesktopPluginThemeSeed,
  DesktopPluginThemeSeedOverrides,
  DesktopPluginEffectSetup,
  DesktopPluginEvents,
  DesktopPluginHost,
  DesktopPluginContributionIcon,
  DesktopPluginIconComponent,
  DesktopPluginIconProps,
  DesktopPluginLocale,
  DesktopPluginLocalProject,
  DesktopPluginMetadata,
  DesktopPluginMainViewContribution,
  DesktopPluginMcp,
  DesktopPluginMcpToolCall,
  DesktopPluginMcpToolResult,
  DesktopPluginNavigation,
  DesktopPluginOpenPathOptions,
  DesktopPluginOpenTarget,
  DesktopPluginOratorio,
  DesktopPluginProjectAction,
  DesktopPluginProjectActionIcon,
  DesktopPluginProjectActions,
  DesktopPluginProjectActionsSnapshot,
  DesktopPluginProjectEnvironment,
  DesktopPluginOratorioBoardEvent,
  DesktopPluginOratorioContext,
  DesktopPluginOratorioEvent,
  DesktopPluginOratorioRequest,
  DesktopPluginOratorioResponse,
  DesktopPluginSavedProjectAction,
  DesktopPluginSession,
  DesktopPluginSessionSnapshot,
  DesktopPluginSettingsPageContribution,
  DesktopPluginSettingsSection,
  DesktopPluginSettingsSectionContext,
  DesktopPluginSettingField,
  DesktopPluginSettingType,
  DesktopPluginSettings,
  DesktopPluginSettingsMutation,
  DesktopPluginSettingsSchema,
  DesktopPluginSettingsScope,
  DesktopPluginSettingsSnapshot,
  DesktopPluginServices,
  DesktopPluginAppSurfaceContext,
  DesktopPluginComposerMascotSurfaceContext,
  DesktopPluginComposerProvider,
  DesktopPluginComposerSurfaceContext,
  DesktopPluginMascotActivity,
  DesktopPluginSubAgent,
  DesktopPluginSubAgents,
  DesktopPluginSubAgentState,
  DesktopPluginThreads,
  DesktopPluginOwnedThread,
  DesktopPluginOwnedThreadConfig,
  DesktopPluginOwnedThreadRequest,
  DesktopPluginLinkedChat,
  DesktopPluginLinkedChatState,
  DesktopPluginSpaceContribution,
  DesktopPluginSpaceGuide,
  DesktopPluginSpaceLocation,
  DesktopPluginSpaceSidebarProps,
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
  DesktopPluginEmptyConversationContext,
  DesktopPluginThreadSurfaceContext,
  DesktopPluginToastOptions,
  DesktopPluginPlanTodo,
  DesktopPluginTurnPlan,
  DesktopPluginTurnRunningTasks,
  DesktopPluginTurnStatusContext,
  DesktopPluginUi,
  DesktopPluginViewProps,
  DesktopPluginWorkspaceReader,
} from "./contracts.js";
export type {
  ToolDisclosureProps,
  RunningShimmerProps,
  LayerBoundaryProps,
  ActionTooltipProps,
  AgentAvatarProps,
  ButtonProps,
  ButtonSize,
  ButtonVariant,
  CatalogFilterGroup,
  CatalogFilterOption,
  CatalogPageBreadcrumb,
  CatalogPageBreadcrumbSegment,
  CatalogPageEditableText,
  CatalogPageFilters,
  CatalogPageTab,
  CatalogPageTabs,
  CatalogPageProps,
  CatalogPageSearch,
  CatalogSectionProps,
  CatalogToolbarButtonProps,
  CheckboxProps,
  ComboboxOption,
  ComboboxProps,
  DesktopPluginUiComponents,
  DesktopSegmentedControlComponent,
  DesktopSelectComponent,
  FieldSize,
  IconButtonProps,
  InlineDiffProps,
  InputProps,
  MarkdownProps,
  UserAvatarProps,
  PersonalMascotProps,
  MenuEntry,
  MenuItem,
  MenuLabel,
  MenuProps,
  MenuSeparator,
  ModalDialogProps,
  ModalFooterProps,
  ModalHeaderProps,
  PillSwitchProps,
  PluginSurfaceComponent,
  PluginSurfaceProps,
  SegmentedControlOption,
  SegmentedControlProps,
  SelectOption,
  SelectProps,
  SkeletonProps,
  SliderProps,
  SpinnerProps,
  SplitButtonItem,
  SplitButtonProps,
  SettingsBreadcrumbProps,
  SettingsGroupProps,
  SettingsPanelShellProps,
  SettingsRowProps,
  SidebarRowProps,
  MascotOutfitProps,
  WorkspaceNameProps,
  SummaryRowProps,
  SummarySectionProps,
  TextareaProps,
} from "./ui.js";

import { readDesktopPluginRuntime } from "./runtime.js";

const ui = readDesktopPluginRuntime().ui;

export const ToolDisclosure = ui.ToolDisclosure;
export const RunningShimmer = ui.RunningShimmer;
export const LayerBoundary = ui.LayerBoundary;
export const PluginSurface = ui.PluginSurface;
export const Button = ui.Button;
export const IconButton = ui.IconButton;
export const Input = ui.Input;
export const Textarea = ui.Textarea;
export const Select = ui.Select;
export const SegmentedControl = ui.SegmentedControl;
export const Checkbox = ui.Checkbox;
export const Spinner = ui.Spinner;
export const Skeleton = ui.Skeleton;
export const Slider = ui.Slider;
export const ActionTooltip = ui.ActionTooltip;
export const Combobox = ui.Combobox;
export const ModalDialog = ui.ModalDialog;
export const ModalFooter = ui.ModalFooter;
export const ModalHeader = ui.ModalHeader;
export const PillSwitch = ui.PillSwitch;
export const SettingsPanelShell = ui.SettingsPanelShell;
export const SettingsBreadcrumb = ui.SettingsBreadcrumb;
export const SettingsGroup = ui.SettingsGroup;
export const SettingsRow = ui.SettingsRow;
export const InlineDiff = ui.InlineDiff;
export const Markdown = ui.Markdown;
export const AgentAvatar = ui.AgentAvatar;
export const UserAvatar = ui.UserAvatar;
export const PersonalMascot = ui.PersonalMascot;
export const Menu = ui.Menu;
export const SplitButton = ui.SplitButton;
export const CatalogPage = ui.CatalogPage;
export const CatalogToolbarButton = ui.CatalogToolbarButton;
export const CatalogSection = ui.CatalogSection;
export const SummarySection = ui.SummarySection;
export const SummaryRow = ui.SummaryRow;
export const SidebarRow = ui.SidebarRow;
export const MascotOutfit = ui.MascotOutfit;
export const WorkspaceName = ui.WorkspaceName;
