import { installMockApi } from '../mockApi'
import { installSandboxProxy } from './sandboxProxy'
import '../styles.css'

installMockApi()
installSandboxProxy()

void import('./entry').then(({ start }) => {
  start()
})
