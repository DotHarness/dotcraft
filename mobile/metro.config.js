const path = require('node:path')
const { getDefaultConfig } = require('expo/metro-config')

const projectRoot = __dirname
const sdkRoot = path.resolve(projectRoot, '..', 'sdk', 'typescript')

const config = getDefaultConfig(projectRoot)

config.watchFolders = [...config.watchFolders, sdkRoot]

const singletons = new Set(['react', 'react-dom', 'react-native', 'react-native-web'])
const appOrigin = path.join(projectRoot, 'package.json')

config.resolver.resolveRequest = (context, moduleName, platform) => {
  const fromOutside = !path.resolve(context.originModulePath).startsWith(projectRoot + path.sep)
  const resolved = fromOutside && singletons.has(moduleName.split('/')[0]) ? { ...context, originModulePath: appOrigin } : context
  return context.resolveRequest(resolved, moduleName, platform)
}

module.exports = config
