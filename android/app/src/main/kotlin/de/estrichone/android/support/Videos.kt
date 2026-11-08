package de.estrichone.android.support

import android.content.Context
import android.media.MediaMetadataRetriever
import android.net.Uri
import androidx.annotation.OptIn
import androidx.media3.common.MediaItem
import androidx.media3.common.MimeTypes
import androidx.media3.common.util.UnstableApi
import androidx.media3.effect.Presentation
import androidx.media3.transformer.Composition
import androidx.media3.transformer.DefaultEncoderFactory
import androidx.media3.transformer.EditedMediaItem
import androidx.media3.transformer.Effects
import androidx.media3.transformer.ExportException
import androidx.media3.transformer.ExportResult
import androidx.media3.transformer.Transformer
import androidx.media3.transformer.VideoEncoderSettings
import de.estrichone.core.VideoTarget
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import java.io.File
import kotlin.coroutines.resume

/**
 * Videos vor dem Speichern verkleinern (wie VideoCompressor auf iOS): höchstens 720p mit etwa 2 Mbit/s, HEVC, wo das
 * Gerät es kodieren kann, sonst H.264; Ton AAC; höchstens 2 Minuten. Rechnerisch rund 15 MB je Minute statt 100 MB
 * und mehr aus der Kamera-App.
 */
object Videos {
    /** Verkleinerte Fassung im Cache oder `null`, wenn das Umkodieren nicht gelingt (dann bleibt das Original). */
    @OptIn(UnstableApi::class)
    suspend fun compress(context: Context, source: Uri): File? {
        val (width, height) = withContext(Dispatchers.IO) { displaySize(context, source) } ?: return null
        val (targetWidth, targetHeight) = VideoTarget.size(width, height)
        val output = File(File(context.cacheDir, "capture").apply { mkdirs() }, "video-${System.currentTimeMillis()}-720p.mp4")
        // Transformer verlangt einen Thread mit Looper; Rückmeldungen kommen auf demselben Thread.
        val ok = withContext(Dispatchers.Main) {
            suspendCancellableCoroutine { continuation ->
                val transformer = Transformer.Builder(context)
                    .setVideoMimeType(MimeTypes.VIDEO_H265)
                    .setAudioMimeType(MimeTypes.AUDIO_AAC)
                    .setEncoderFactory(
                        DefaultEncoderFactory.Builder(context)
                            .setRequestedVideoEncoderSettings(VideoEncoderSettings.Builder().setBitrate(VideoTarget.VIDEO_BITRATE).build())
                            // Ohne HEVC-Kodierer: H.264 bzw. die nächstmögliche Einstellung des Geräts.
                            .setEnableFallback(true)
                            .build(),
                    )
                    .addListener(object : Transformer.Listener {
                        override fun onCompleted(composition: Composition, exportResult: ExportResult) {
                            if (continuation.isActive) continuation.resume(true)
                        }

                        override fun onError(composition: Composition, exportResult: ExportResult, exportException: ExportException) {
                            if (continuation.isActive) continuation.resume(false)
                        }
                    })
                    .build()
                val item = MediaItem.Builder()
                    .setUri(source)
                    .setClippingConfiguration(MediaItem.ClippingConfiguration.Builder().setEndPositionMs(VideoTarget.MAX_DURATION_MS).build())
                    .build()
                val edited = EditedMediaItem.Builder(item)
                    .setEffects(Effects(emptyList(), listOf(Presentation.createForWidthAndHeight(targetWidth, targetHeight, Presentation.LAYOUT_SCALE_TO_FIT))))
                    .build()
                transformer.start(edited, output.absolutePath)
            }
        }
        if (!ok || output.length() == 0L) {
            output.delete()
            return null
        }
        return output
    }

    /** Breite und Höhe wie angezeigt (Drehung berücksichtigt). */
    private fun displaySize(context: Context, source: Uri): Pair<Int, Int>? = runCatching {
        MediaMetadataRetriever().run {
            try {
                setDataSource(context, source)
                val width = extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_WIDTH)?.toIntOrNull() ?: return@run null
                val height = extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_HEIGHT)?.toIntOrNull() ?: return@run null
                val rotation = extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION)?.toIntOrNull() ?: 0
                if (rotation % 180 != 0) height to width else width to height
            } finally {
                release()
            }
        }
    }.getOrNull()
}
