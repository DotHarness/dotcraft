import * as Application from 'expo-application'
import Constants from 'expo-constants'
import * as Device from 'expo-device'
import { Platform } from 'react-native'
import type { DeviceInfo } from '../core/session'

export const appVersion = Application.nativeApplicationVersion ?? Constants.expoConfig?.version ?? '0.0.0'
export const appBuild = Application.nativeBuildVersion

export function deviceInfo(): DeviceInfo {
  return {
    displayName: Device.deviceName?.trim() || Device.modelName || 'Phone',
    platform: Platform.OS === 'ios' ? 'ios' : 'android',
    osVersion: Device.osVersion ?? String(Platform.Version),
    appVersion,
  }
}
