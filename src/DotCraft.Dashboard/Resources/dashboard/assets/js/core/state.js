export const state = {
  panel: 'overview',
  sessions: [],
  available: { dreams: false, automations: false },
  runtime: {
    readOnly: false,
    workspacePath: '',
    capabilities: { settings: true, dreams: true, automations: true, sessionDeletion: true }
  }
};

export function capability(name) {
  return state.runtime.capabilities[name] !== false;
}
