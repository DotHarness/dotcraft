import { Button, type DesktopPluginActivate, type DesktopPluginViewProps } from "@dotcraft/plugin";
import "./index.css";

function MainView({ host }: DesktopPluginViewProps) {
  return (
    <main className="plugin-main-view">
      <h1>[TODO: name]</h1>
      <Button onClick={() => host.ui.showToast({ message: "[TODO: name] is active." })}>
        [TODO: replace this view with the real one]
      </Button>
    </main>
  );
}

export const activate: DesktopPluginActivate = () => ({
  mainViews: [
    {
      id: "main",
      label: { default: "[TODO: name]" },
      order: 80,
      component: MainView,
    },
  ],
});
