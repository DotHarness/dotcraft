export function activate(host) {
  return {
    mainViews: {
      board: host.components.OratorioView
    },
    settingsPanels: {
      oratorio: host.components.OratorioSettingsPanel
    }
  }
}
