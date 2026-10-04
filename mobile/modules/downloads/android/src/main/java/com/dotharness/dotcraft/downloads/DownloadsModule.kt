package com.dotharness.dotcraft.downloads

import android.content.ContentValues
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.util.Base64
import expo.modules.kotlin.exception.CodedException
import expo.modules.kotlin.exception.Exceptions
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition

class DownloadsModule : Module() {
  override fun definition() = ModuleDefinition {
    Name("Downloads")

    AsyncFunction("save") { dataBase64: String, fileName: String, mimeType: String ->
      if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) {
        throw CodedException("UNSUPPORTED", "Saving to Downloads needs Android 10 or later.", null)
      }
      val resolver = (appContext.reactContext ?: throw Exceptions.ReactContextLost()).contentResolver
      val pending = ContentValues().apply {
        put(MediaStore.MediaColumns.DISPLAY_NAME, fileName)
        put(MediaStore.MediaColumns.MIME_TYPE, mimeType)
        put(MediaStore.MediaColumns.RELATIVE_PATH, "${Environment.DIRECTORY_DOWNLOADS}/DotCraft")
        put(MediaStore.MediaColumns.IS_PENDING, 1)
      }
      val uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, pending)
        ?: throw CodedException("SAVE_FAILED", "Couldn't create the download.", null)
      try {
        val stream = resolver.openOutputStream(uri)
          ?: throw CodedException("SAVE_FAILED", "Couldn't write the download.", null)
        stream.use { it.write(Base64.decode(dataBase64, Base64.DEFAULT)) }
        resolver.update(uri, ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }, null, null)
      } catch (error: Exception) {
        resolver.delete(uri, null, null)
        throw error
      }
      resolver.query(uri, arrayOf(MediaStore.MediaColumns.DISPLAY_NAME), null, null, null)?.use {
        if (it.moveToFirst()) it.getString(0) else null
      } ?: fileName
    }
  }
}
