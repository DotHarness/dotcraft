// The docs site serves the web demo under a sub-path, which the export takes from DOTCRAFT_WEB_BASE_URL.
module.exports = ({ config }) => {
  const baseUrl = process.env.DOTCRAFT_WEB_BASE_URL
  return baseUrl ? { ...config, experiments: { ...config.experiments, baseUrl } } : config
}
