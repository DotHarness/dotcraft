import { NativeModule, requireNativeModule } from 'expo'

declare class DownloadsModule extends NativeModule {
  save(dataBase64: string, fileName: string, mimeType: string): Promise<string>
}

export default requireNativeModule<DownloadsModule>('Downloads')
