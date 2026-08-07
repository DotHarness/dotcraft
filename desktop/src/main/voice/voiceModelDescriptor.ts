export interface ManagedVoiceModelDescriptor {
  id: string
  fileName: string
  revision: string
  sha256: string
  displayBytes: number
  downloadUrl: string
}

export const MANAGED_VOICE_MODEL: ManagedVoiceModelDescriptor = {
  id: 'whisper-base-multilingual-v1',
  fileName: 'ggml-base.bin',
  revision: '80da2d8bfee42b0e836fc3a9890373e5defc00a6',
  sha256: '60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe',
  displayBytes: 147_951_465,
  downloadUrl:
    'https://huggingface.co/ggerganov/whisper.cpp/resolve/80da2d8bfee42b0e836fc3a9890373e5defc00a6/ggml-base.bin'
}
