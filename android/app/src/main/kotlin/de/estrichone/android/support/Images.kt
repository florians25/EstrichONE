package de.estrichone.android.support

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.pdf.PdfDocument
import android.media.ExifInterface
import android.util.Base64
import de.estrichone.core.DemoData
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import kotlin.math.max
import kotlin.random.Random

/** Bilder verkleinern, drehen, als JPEG speichern; PDF aus Scans; Beispielbilder des Demomodus. */
object Images {
    /** Größte Kante eines hochgeladenen Fotos (wie iOS PhotoCompressor). */
    const val MAX_EDGE = 2560

    /** Verkleinert beim Dekodieren (spart Speicher bei großen Fotos) und beachtet die EXIF-Ausrichtung. */
    fun decode(bytes: ByteArray, size: Int, applyExif: Boolean = true): Bitmap? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        if (bounds.outWidth <= 0) return null
        var sample = 1
        while (max(bounds.outWidth, bounds.outHeight) / (sample * 2) >= size) sample *= 2
        val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sample }) ?: return null
        return if (applyExif) rotate(bitmap, orientation(bytes)) else bitmap
    }

    private fun orientation(bytes: ByteArray): Int =
        runCatching { ExifInterface(ByteArrayInputStream(bytes)).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL) }
            .getOrDefault(ExifInterface.ORIENTATION_NORMAL)

    private fun rotate(bitmap: Bitmap, orientation: Int): Bitmap {
        val degrees = when (orientation) {
            ExifInterface.ORIENTATION_ROTATE_90 -> 90f
            ExifInterface.ORIENTATION_ROTATE_180 -> 180f
            ExifInterface.ORIENTATION_ROTATE_270 -> 270f
            else -> return bitmap
        }
        return Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, Matrix().apply { postRotate(degrees) }, true)
    }

    /** Foto für den Upload: aufrecht, höchstens [MAX_EDGE] Pixel, JPEG-Qualität 85. */
    fun preparePhoto(bytes: ByteArray, extraRotation: Int = 0): ByteArray {
        // Kamera im Speicher: Drehung kommt aus CameraX, nicht aus EXIF (sonst doppelt gedreht).
        val decoded = decode(bytes, MAX_EDGE, applyExif = extraRotation == 0) ?: return bytes
        val bitmap = if (extraRotation % 360 != 0) {
            Bitmap.createBitmap(decoded, 0, 0, decoded.width, decoded.height, Matrix().apply { postRotate(extraRotation.toFloat()) }, true)
        } else decoded
        val scale = MAX_EDGE.toFloat() / max(bitmap.width, bitmap.height)
        val scaled = if (scale < 1f) Bitmap.createScaledBitmap(bitmap, (bitmap.width * scale).toInt(), (bitmap.height * scale).toInt(), true) else bitmap
        return jpeg(scaled, 85)
    }

    fun jpeg(bitmap: Bitmap, quality: Int = 85): ByteArray = ByteArrayOutputStream().also { bitmap.compress(Bitmap.CompressFormat.JPEG, quality, it) }.toByteArray()

    fun pngBase64(bitmap: Bitmap): String =
        Base64.encodeToString(ByteArrayOutputStream().also { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }.toByteArray(), Base64.NO_WRAP)

    /** Gescannte Seiten (Fotos) als PDF in A4-Proportionen (wie ScanPDFBuilder auf iOS). */
    fun pdf(pages: List<ByteArray>): ByteArray {
        val document = PdfDocument()
        pages.forEachIndexed { index, bytes ->
            val bitmap = decode(bytes, 2000) ?: return@forEachIndexed
            val width = 595
            val height = 842
            val page = document.startPage(PdfDocument.PageInfo.Builder(width, height, index + 1).create())
            val scale = minOf(width.toFloat() / bitmap.width, height.toFloat() / bitmap.height)
            val dx = (width - bitmap.width * scale) / 2
            val dy = (height - bitmap.height * scale) / 2
            page.canvas.drawColor(Color.WHITE)
            page.canvas.drawBitmap(bitmap, Matrix().apply { postScale(scale, scale); postTranslate(dx, dy) }, Paint(Paint.FILTER_BITMAP_FLAG))
            document.finishPage(page)
        }
        val out = ByteArrayOutputStream()
        document.writeTo(out)
        document.close()
        return out.toByteArray()
    }

    // Demomodus

    private val cache = HashMap<String, ByteArray>()

    /** Inhalt einer Beispieldatei: ruhige Motive ohne Text bzw. ein Lieferschein als PDF. */
    fun demoContent(id: String): ByteArray? = synchronized(cache) {
        cache[id]?.let { return it }
        val result = if (id == DemoData.PDF) demoPdf() else DemoData.photos.firstOrNull { it.id == id }?.let { jpeg(demoPhoto(it.motif), 80) }
        result?.also { cache[id] = it }
    }

    fun demoPhoto(motif: DemoData.Motif): Bitmap {
        val bitmap = Bitmap.createBitmap(1200, 900, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        val random = Random(motif.ordinal * 13 + 7)
        fun fill(color: Int, l: Float, t: Float, r: Float, b: Float) {
            paint.color = color
            paint.style = Paint.Style.FILL
            canvas.drawRect(l, t, r, b, paint)
        }
        fun grain(l: Float, t: Float, r: Float, b: Float, light: Int, count: Int) {
            repeat(count) {
                paint.color = Color.argb(random.nextInt(10, 30), light, light, light)
                val x = l + random.nextFloat() * (r - l)
                val y = t + random.nextFloat() * (b - t)
                canvas.drawRect(x, y, x + 6, y + 6, paint)
            }
        }
        when (motif) {
            DemoData.Motif.EdgeStrip -> {
                fill(Color.rgb(219, 214, 204), 0f, 0f, 1200f, 520f)
                fill(Color.rgb(140, 143, 148), 0f, 520f, 1200f, 900f)
                fill(Color.rgb(237, 237, 230), 0f, 470f, 1200f, 530f)
                grain(0f, 530f, 1200f, 900f, 50, 1500)
            }
            DemoData.Motif.Heating -> {
                fill(Color.rgb(204, 204, 199), 0f, 0f, 1200f, 900f)
                grain(0f, 0f, 1200f, 900f, 255, 900)
                paint.color = Color.rgb(199, 51, 41)
                paint.style = Paint.Style.STROKE
                paint.strokeWidth = 26f
                paint.strokeCap = Paint.Cap.ROUND
                for (row in 0 until 6) canvas.drawLine(90f, 110f + row * 130, 1110f, 110f + row * 130, paint)
            }
            DemoData.Motif.Screed -> {
                fill(Color.rgb(158, 158, 153), 0f, 0f, 1200f, 900f)
                grain(0f, 0f, 1200f, 900f, 242, 2200)
                grain(0f, 0f, 1200f, 900f, 38, 1200)
                fill(Color.rgb(230, 224, 214), 0f, 0f, 1200f, 140f)
            }
            DemoData.Motif.Pump -> {
                fill(Color.rgb(158, 179, 199), 0f, 0f, 1200f, 430f)
                fill(Color.rgb(77, 79, 84), 0f, 430f, 1200f, 900f)
                grain(0f, 430f, 1200f, 900f, 204, 1500)
                fill(Color.rgb(237, 140, 26), 300f, 330f, 860f, 590f)
                fill(Color.rgb(31, 31, 33), 340f, 590f, 460f, 710f)
                fill(Color.rgb(31, 31, 33), 700f, 590f, 820f, 710f)
            }
            DemoData.Motif.Crack -> {
                fill(Color.rgb(168, 166, 158), 0f, 0f, 1200f, 900f)
                grain(0f, 0f, 1200f, 900f, 242, 1800)
                paint.color = Color.rgb(46, 46, 46)
                paint.style = Paint.Style.STROKE
                paint.strokeWidth = 5f
                val path = Path().apply {
                    moveTo(150f, 820f); lineTo(380f, 610f); lineTo(450f, 640f); lineTo(700f, 400f); lineTo(760f, 430f); lineTo(1050f, 120f)
                }
                canvas.drawPath(path, paint)
            }
            DemoData.Motif.Access -> {
                fill(Color.rgb(140, 173, 204), 0f, 0f, 1200f, 380f)
                fill(Color.rgb(92, 128, 84), 0f, 380f, 1200f, 540f)
                fill(Color.rgb(69, 71, 77), 0f, 540f, 1200f, 900f)
                grain(0f, 540f, 1200f, 900f, 217, 1400)
                fill(Color.rgb(217, 219, 224), 640f, 180f, 1100f, 420f)
            }
        }
        return bitmap
    }

    private fun demoPdf(): ByteArray {
        val document = PdfDocument()
        val page = document.startPage(PdfDocument.PageInfo.Builder(595, 842, 1).create())
        val c = page.canvas
        c.drawColor(Color.WHITE)
        val bold = Paint(Paint.ANTI_ALIAS_FLAG).apply { textSize = 26f; isFakeBoldText = true; color = Color.BLACK }
        val body = Paint(Paint.ANTI_ALIAS_FLAG).apply { textSize = 15f; color = Color.DKGRAY }
        c.drawText("Lieferschein 4711", 48f, 80f, bold)
        listOf("Baustelle: EFH Schneider, Ahornweg 3, 84028 Landshut", "Material: Zementestrich CT-C25-F4", "Menge: 18 m³",
            "Anlieferung: 07:15 Uhr", "Fahrer: K. Bauer").forEachIndexed { i, line -> c.drawText(line, 48f, 140f + i * 32, body) }
        document.finishPage(page)
        val out = ByteArrayOutputStream()
        document.writeTo(out)
        document.close()
        return out.toByteArray()
    }
}
