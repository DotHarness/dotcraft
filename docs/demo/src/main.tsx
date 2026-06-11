import { installMockApi } from './mockApi'
import './styles.css'

// Install the mock preload bridge before any Desktop renderer module loads.
installMockApi()

void import('./appEntry').then(({ start }) => {
  start()
})
