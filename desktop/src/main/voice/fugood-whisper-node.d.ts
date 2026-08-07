declare module '@fugood/whisper.node' {
  export interface WhisperTranscriptionResult {
    language?: string
    result: string
    segments: Array<{ text: string; t0: number; t1: number }>
    isAborted: boolean
  }

  export interface WhisperContext {
    transcribeFile(
      filePath: string,
      options?: {
        language?: string
        temperature?: number
        maxThreads?: number
      }
    ): {
      stop(): Promise<void>
      promise: Promise<WhisperTranscriptionResult>
    }
    release(): Promise<void>
  }

  export function initWhisper(
    options: { filePath: string; useGpu?: boolean },
    variant?: 'default' | 'vulkan' | 'cuda'
  ): Promise<WhisperContext>
}
