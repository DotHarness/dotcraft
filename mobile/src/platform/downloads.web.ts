export async function saveToDownloads(dataBase64: string, fileName: string, mimeType: string): Promise<string> {
  const link = document.createElement('a')
  link.href = `data:${mimeType};base64,${dataBase64}`
  link.download = fileName
  link.click()
  return fileName
}

export function isUnsupportedSave(): boolean {
  return false
}

export function showToast(): void {}
