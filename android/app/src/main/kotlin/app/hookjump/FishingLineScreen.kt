package app.hookjump

import android.provider.Settings
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.systemBars
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.withFrameMillis
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.hookjump.booth.Chart
import app.hookjump.booth.clock
import app.hookjump.booth.fraction
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlin.math.sin
import kotlinx.coroutines.delay

// The fishing-line chart: the song is a line
// going down into the water from 0:00, a float where playback is, a red hook
// where the jump goes, ticks where the listener rewound to (right) and skipped
// to (left). One fixed look on a pale sea, in light and dark alike.

private val Sea = Color(0xFFDCEAE6)
private val Ink = Color(0xFF12304A)
private val Buoy = Color(0xFFB5301F)
private val Contour = Color(0xFFB4D0CA)
private val Kelp = Color(0xFF2F7D73)
private val Foam = Color(0xFFF4F8F6)

private val Barlow = FontFamily(
    Font(R.font.barlow_condensed_medium, FontWeight.Medium),
    Font(R.font.barlow_condensed_semibold, FontWeight.SemiBold),
    Font(R.font.barlow_condensed_bold, FontWeight.Bold),
)
private val Plex = FontFamily(
    Font(R.font.ibm_plex_mono_regular, FontWeight.Normal),
    Font(R.font.ibm_plex_mono_medium, FontWeight.Medium),
)

private val LINE_X = 110.dp
private val LINE_MARGIN = 28.dp

// The cast of a new pin: the whole of it, and the parts spent in flight and splash.
private const val CAST_MS = 1_300
private const val FLIGHT = 0.27f
private const val SPLASH = 0.12f

// The reel of a removed pin: the hook rises for the first part, then flies off.
private const val REEL_MS = 1_200
private const val RISE = 0.55f

@Composable
fun FishingLineScreen(
    access: Boolean,
    chart: Chart?,
    onOpenAccess: () -> Unit,
    onJump: () -> Unit,
    onPin: () -> Unit,
    onUnpin: () -> Unit,
    onSong: (SongControl) -> Unit,
) {
    Column(Modifier.fillMaxSize().background(Sea).windowInsetsPadding(WindowInsets.systemBars)) {
        when {
            !access -> Message(
                "hookjump needs notification access",
                "That access is how it sees what Spotify plays. What it learns never leaves your phone.",
                "Allow access" to onOpenAccess,
            )
            chart == null -> Message("Nothing playing", "Play a song in Spotify, and it shows here as a line.", null)
            else -> {
                Header(chart)
                FishingLine(chart, onUnpin, Modifier.weight(1f).fillMaxWidth())
                Transport(chart.playing, onSong)
                Buttons(onJump, onPin)
            }
        }
    }
}

@Composable
private fun Header(chart: Chart) {
    Column(Modifier.padding(start = 24.dp, end = 24.dp, top = 20.dp)) {
        BasicText(
            chart.title,
            style = TextStyle(fontFamily = Barlow, fontWeight = FontWeight.Bold, fontSize = 44.sp, lineHeight = 44.sp, color = Ink),
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
        )
        chart.artist?.let {
            BasicText(
                it,
                style = TextStyle(fontFamily = Barlow, fontWeight = FontWeight.Medium, fontSize = 19.sp, color = Ink),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun FishingLine(chart: Chart, onUnpin: () -> Unit, modifier: Modifier) {
    // A clock for the float: it moves on between Spotify's reports.
    val wall by produceState(System.currentTimeMillis()) {
        while (true) {
            value = System.currentTimeMillis()
            delay(200)
        }
    }
    val now = chart.nowAt(wall)

    // Motion, slow and calm, and none at all when the phone's animations are off.
    val context = LocalContext.current
    val motion = remember { Settings.Global.getFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f) > 0f }
    // The sea's own clock runs only while the song plays: paused, the water
    // stops where it is, and no frame is drawn at all, so it costs nothing.
    val seaClock = remember { mutableLongStateOf(0L) }
    LaunchedEffect(chart.playing, motion) {
        if (!chart.playing || !motion) return@LaunchedEffect
        var last = withFrameMillis { it }
        while (true) {
            withFrameMillis { frame ->
                seaClock.longValue += frame - last
                last = frame
            }
        }
    }
    fun cycle(periodMs: Long) = (seaClock.longValue % periodMs).toFloat() / periodMs
    val swell = cycle(7_000)
    val drift = cycle(16_000)
    val breath = cycle(9_000)
    val bob = cycle(2_600)
    val current = cycle(11_000)
    val bubbles = cycle(7_000)

    // A new pin is a cast: the hook flies in over the water, splashes in at
    // the surface, sinks along the line to its second, and a ring spreads.
    // A removed pin is reeled in: the hook rises to the surface and flies off.
    // Which one is decided while composing the very frame that shows the
    // change, so no frame shows the new state before its motion starts.
    val seen = remember { arrayOf(HookWords.of(chart)) }
    val change = remember(chart.title, chart.hookMs, chart.pinned) {
        val before = seen[0]
        val after = HookWords.of(chart)
        seen[0] = after
        when {
            !motion || before.title != after.title -> null
            after.pinned && after.ms != null && after.ms != before.ms -> HookChange(cast = true, before = before)
            before.pinned && !after.pinned && before.ms != null -> HookChange(cast = false, before = before)
            else -> null
        }
    }
    val progress = remember(change) { Animatable(if (change == null) 1f else 0f) }
    val ripple = remember { Animatable(1f) }
    LaunchedEffect(change) {
        if (change == null) return@LaunchedEffect
        progress.animateTo(1f, tween(if (change.cast) CAST_MS else REEL_MS, easing = LinearEasing))
        if (change.cast) {
            ripple.snapTo(0f)
            ripple.animateTo(1f, tween(1_100))
        }
    }

    BoxWithConstraints(modifier.semantics { contentDescription = describe(chart, now) }) {
        val density = LocalDensity.current
        fun px(dp: Dp) = with(density) { dp.toPx() }
        val width = constraints.maxWidth.toFloat()
        val top = px(LINE_MARGIN)
        val bottom = constraints.maxHeight - px(LINE_MARGIN)
        val x = px(LINE_X)
        fun y(ms: Long) = top + (bottom - top) * fraction(ms, chart.durationMs).toFloat()
        val hookY = chart.hookMs?.let(::y)
        val nowY = now?.let(::y)
        // The reel: after a seek or a jump the float glides to its new place;
        // along normal playback it simply follows.
        val reeled by animateFloatAsState(
            nowY ?: top,
            if (motion) spring(dampingRatio = 0.85f, stiffness = Spring.StiffnessVeryLow) else tween(0),
            label = "reel",
        )
        val floatY = if (nowY == null) null else reeled
        val wave = if (motion) (2 * PI * bob).toFloat() else 0f
        val bobY = if (motion) sin(wave) * px(1.6.dp) else 0f
        val tilt = if (motion) sin(wave + 0.6f) * 4f else 0f
        // The current bends the line a little: fixed at the surface and at the
        // end, most in the middle. Everything on the line goes with it.
        val sway = if (motion) sin((2 * PI * current).toFloat()) * px(5.dp) else 0f
        fun lx(py: Float): Float = x + sway * sin((PI * ((py - top) / (bottom - top)).coerceIn(0f, 1f)).toFloat())
        fun line(from: Float, to: Float) = Path().apply {
            moveTo(lx(from), from)
            var py = from
            while (py < to) {
                py = minOf(to, py + px(8.dp))
                lineTo(lx(py), py)
            }
        }
        // The hook over the water: from the upper right (t = 0) to the surface (t = 1).
        fun flight(t: Float): Pair<Offset, Float> {
            val sx = minOf(width - px(30.dp), x + px(170.dp))
            val sy = top - px(6.dp)
            val fx = sx + (lx(top) - sx) * t
            val fy = sy + (top - sy) * t - sin(PI.toFloat() * t) * px(48.dp)
            return Offset(fx, fy - px(10.dp)) to 360f * t
        }
        val step = progress.value
        val casting = change != null && change.cast && hookY != null && step < 1f
        val reeling = change != null && !change.cast && step < 1f
        val castAt = step
        val reelAt = step
        var hookAt: Float? = hookY // the hook in the water
        var airHook: Pair<Offset, Float>? = null // the hook in the air, and its turn
        var splash: Float? = null // the splash at the surface, 0 to 1
        var wordsAlpha = 1f
        if (casting) {
            if (castAt < FLIGHT) {
                hookAt = null
                airHook = flight(castAt / FLIGHT)
            } else {
                val t = (castAt - FLIGHT) / (1f - FLIGHT)
                hookAt = top + (hookY!! - top) * (1f - (1f - t) * (1f - t) * (1f - t))
                if (castAt < FLIGHT + SPLASH) splash = (castAt - FLIGHT) / SPLASH
            }
            wordsAlpha = ((castAt - 0.85f) / 0.15f).coerceIn(0f, 1f)
        } else if (reeling) {
            val from = y(change!!.before.ms!!)
            if (reelAt < RISE) {
                val t = reelAt / RISE
                hookAt = from - (from - top) * t * t * t
            } else {
                hookAt = null
                airHook = flight(1f - (reelAt - RISE) / (1f - RISE))
                if (reelAt < RISE + SPLASH) splash = (reelAt - RISE) / SPLASH
            }
            wordsAlpha = ((reelAt - 0.85f) / 0.15f).coerceIn(0f, 1f) // the new words, after
        }
        val hookDrawY = hookAt
        // The old words fade out at the start of a cast or a reel.
        val oldWordsAlpha = if ((casting || reeling) && step < 0.15f) 1f - step / 0.15f else 0f
        val drawnAir = airHook
        val drawnSplash = splash

        Canvas(Modifier.fillMaxSize()) {
            // Depth contours: the sea, not data. A current moves them sideways
            // and each one swells and settles in its own time.
            for (i in 0 until 6) {
                val cy = top + (i + 0.6f) * (bottom - top) / 6
                val swellOf = if (motion) 0.75f + 0.35f * sin((2 * PI * (breath + i * 0.17)).toFloat()) else 1f
                val a = px(26.dp) * swellOf * if (i % 2 == 0) 1 else -1
                val dx = if (motion) sin((2 * PI * (drift + i / 6.0)).toFloat()) * px(22.dp) else 0f
                val path = Path().apply {
                    moveTo(-px(30.dp) + dx, cy)
                    cubicTo(width * 0.2f + dx, cy - a, width * 0.4f + dx, cy + a, width * 0.62f + dx, cy)
                    cubicTo(width * 0.8f + dx, cy - a * 0.8f, width * 0.92f + dx, cy + a * 0.4f, width + px(30.dp) + dx, cy - a * 0.2f)
                }
                drawPath(path, Contour, style = Stroke(px(1.2.dp)))
            }
            // The surface: 0:00. Its waves roll by.
            val step = px(32.dp)
            val surface = Path().apply {
                var sx = -2 * step + if (motion) swell * 2 * step else 0f
                moveTo(sx, top)
                var up = true
                while (sx < width) {
                    quadraticTo(sx + step / 2, top + if (up) -px(5.dp) else px(5.dp), sx + step, top)
                    sx += step
                    up = !up
                }
            }
            drawPath(surface, Ink, style = Stroke(px(2.dp)))

            // Minutes, as soundings on the chart.
            var minute = 60_000L
            while (minute < chart.durationMs) {
                val my = y(minute)
                drawLine(Ink, Offset(x - px(48.dp), my), Offset(x - px(36.dp), my), px(1.5.dp))
                minute += 60_000L
            }

            // The line: solid down to the hook, dotted after it.
            drawPath(line(top, hookDrawY ?: bottom), Ink, style = Stroke(px(2.dp)))
            if (hookDrawY != null) {
                val dots = PathEffect.dashPathEffect(floatArrayOf(px(3.dp), px(7.dp)))
                drawPath(line(hookDrawY, bottom), Ink, style = Stroke(px(2.dp), cap = StrokeCap.Round, pathEffect = dots))
            }

            fun hookShape(hx: Float, hy: Float, turn: Float) = rotate(turn, Offset(hx, hy + px(10.dp))) {
                val hook = Path().apply {
                    moveTo(hx, hy)
                    lineTo(hx, hy + px(18.dp))
                    arcTo(Rect(hx - px(22.dp), hy + px(7.dp), hx, hy + px(29.dp)), 0f, 180f, false)
                    lineTo(hx - px(22.dp), hy + px(12.dp))
                    lineTo(hx - px(16.dp), hy + px(18.dp))
                }
                drawPath(hook, Buoy, style = Stroke(px(3.5.dp), cap = StrokeCap.Round, join = StrokeJoin.Round))
                drawCircle(Sea, px(4.5.dp), Offset(hx, hy - px(3.dp)))
                drawCircle(Buoy, px(4.5.dp), Offset(hx, hy - px(3.dp)), style = Stroke(px(3.dp)))
            }

            // A cast or a reel: the hook in the air, and the splash at the surface.
            drawnAir?.let { (at, turn) -> hookShape(at.x, at.y, turn) }
            drawnSplash?.let { q ->
                drawCircle(Ink.copy(alpha = 0.8f * (1f - q)), px(4.dp) + px(16.dp) * q, Offset(lx(top), top), style = Stroke(px(1.5.dp)))
            }

            // Where the listener rewound to (right) and skipped to (left).
            for (ms in chart.rewinds) {
                val ty = y(ms)
                drawLine(Ink, Offset(lx(ty) + px(10.dp), ty), Offset(lx(ty) + px(32.dp), ty), px(3.dp), StrokeCap.Round)
            }
            for (ms in chart.skips) {
                val ty = y(ms)
                drawLine(Kelp, Offset(lx(ty) - px(10.dp), ty), Offset(lx(ty) - px(24.dp), ty), px(3.dp), StrokeCap.Round)
            }

            if (hookDrawY != null) {
                val hx = lx(hookDrawY)
                // Bubbles rise from the hook while the song plays.
                if (motion && chart.playing && !casting && !reeling) {
                    for (k in 0 until 3) {
                        val t = (bubbles - k * 0.09f) / 0.45f
                        if (t !in 0f..1f) continue
                        val by = hookDrawY - px(64.dp) * t
                        val bx = lx(by) + px(9.dp) + sin(t * 2 * PI.toFloat() + k) * px(3.dp)
                        drawCircle(Ink.copy(alpha = 0.45f * (1f - t)), px(2.2.dp + 0.6.dp * k), Offset(bx, by), style = Stroke(px(1.2.dp)))
                    }
                }
                // The hook.
                hookShape(hx, hookDrawY, 0f)
                // The pin's ripple, once the hook has settled.
                if (ripple.value < 1f && !reeling) {
                    val r = ripple.value
                    drawCircle(Buoy.copy(alpha = 1f - r), px(8.dp) + px(44.dp) * r, Offset(hx, hookDrawY), style = Stroke(px(2.dp)))
                }
            }

            // The float, where playback is: it bobs while the song plays.
            if (floatY != null) {
                val fy = floatY + bobY
                val fx = lx(fy)
                rotate(tilt, Offset(fx, fy)) {
                    drawOval(Buoy, Offset(fx - px(10.dp), fy - px(15.dp)), Size(px(20.dp), px(30.dp)))
                    drawRect(Foam, Offset(fx - px(10.dp), fy - px(2.dp)), Size(px(20.dp), px(4.dp)))
                }
            }
        }

        val mono = TextStyle(fontFamily = Plex, fontSize = 13.sp, color = Ink)
        fun at(dx: Dp, py: Float) = Modifier.offset { IntOffset(px(dx).roundToInt(), py.roundToInt()) }
        BasicText(clock(chart.durationMs), at(20.dp, bottom - px(10.dp)), style = mono)
        var minute = 60_000L
        while (minute < chart.durationMs) {
            val my = y(minute)
            if (my - top > px(22.dp) && bottom - my > px(22.dp)) BasicText(clock(minute), at(22.dp, my - px(10.dp)), style = mono)
            minute += 60_000L
        }
        // The time at the float, unless the float is at the hook: there the
        // hook's own time says it, and the float shows where playback is.
        if (now != null && floatY != null && (hookY == null || abs(floatY - hookY) >= px(56.dp))) {
            BasicText(
                clock(now),
                at(LINE_X + 26.dp, floatY - px(11.dp)),
                style = TextStyle(fontFamily = Plex, fontWeight = FontWeight.Medium, fontSize = 16.sp, color = Ink),
            )
        }
        // The hook's words: the old ones fade out as a cast or a reel starts,
        // the new ones come once it is done.
        fun wordsTop(ms: Long?) = (ms?.let(::y) ?: (top + px(40.dp))) - px(28.dp)
        if (oldWordsAlpha > 0f && change != null) {
            WordsBlock(change.before.ms, change.before.label, change.before.detail, change.before.pinned, oldWordsAlpha, wordsTop(change.before.ms), null)
        }
        WordsBlock(chart.hookMs, chart.hookLabel, chart.hookDetail, chart.pinned, wordsAlpha, wordsTop(chart.hookMs), onUnpin)
    }
}

@Composable
private fun WordsBlock(ms: Long?, label: String, detail: String, pinned: Boolean, alpha: Float, topPx: Float, onUnpin: (() -> Unit)?) {
    val density = LocalDensity.current
    Column(
        Modifier
            .offset { IntOffset(with(density) { (LINE_X + 46.dp).toPx() }.roundToInt(), topPx.roundToInt()) }
            .width(220.dp)
            .alpha(alpha),
    ) {
        ms?.let {
            BasicText(clock(it), style = TextStyle(fontFamily = Plex, fontWeight = FontWeight.Medium, fontSize = 26.sp, color = Buoy))
        }
        BasicText(label, style = TextStyle(fontFamily = Barlow, fontWeight = FontWeight.SemiBold, fontSize = 19.sp, lineHeight = 21.sp, color = Ink))
        BasicText(detail, style = TextStyle(fontFamily = Barlow, fontWeight = FontWeight.Medium, fontSize = 15.sp, color = Ink.copy(alpha = 0.75f)))
        if (pinned) {
            // Only the settled words take a press: a fading link does nothing.
            val press = if (onUnpin != null && alpha >= 1f) Modifier.clickable(role = Role.Button, onClick = onUnpin) else Modifier
            Box(Modifier.height(44.dp).then(press), contentAlignment = Alignment.CenterStart) {
                BasicText(
                    "Remove pin",
                    style = TextStyle(
                        fontFamily = Barlow, fontWeight = FontWeight.SemiBold, fontSize = 17.sp,
                        color = Buoy, textDecoration = TextDecoration.Underline,
                    ),
                )
            }
        }
    }
}

/** What the song buttons ask Spotify to do. */
enum class SongControl { PREVIOUS, PLAY_PAUSE, NEXT }

@Composable
private fun Transport(playing: Boolean, onSong: (SongControl) -> Unit) {
    Row(
        Modifier.fillMaxWidth().padding(top = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(28.dp, Alignment.CenterHorizontally),
    ) {
        SongButton("Previous song", { onSong(SongControl.PREVIOUS) }) { s ->
            drawRect(Ink, Offset(s * 0.18f, s * 0.2f), Size(s * 0.1f, s * 0.6f))
            drawPath(Path().apply { moveTo(s * 0.82f, s * 0.2f); lineTo(s * 0.82f, s * 0.8f); lineTo(s * 0.32f, s * 0.5f); close() }, Ink)
        }
        SongButton(if (playing) "Pause" else "Play", { onSong(SongControl.PLAY_PAUSE) }) { s ->
            if (playing) {
                drawRect(Ink, Offset(s * 0.26f, s * 0.2f), Size(s * 0.16f, s * 0.6f))
                drawRect(Ink, Offset(s * 0.58f, s * 0.2f), Size(s * 0.16f, s * 0.6f))
            } else {
                drawPath(Path().apply { moveTo(s * 0.3f, s * 0.18f); lineTo(s * 0.3f, s * 0.82f); lineTo(s * 0.82f, s * 0.5f); close() }, Ink)
            }
        }
        SongButton("Next song", { onSong(SongControl.NEXT) }) { s ->
            drawRect(Ink, Offset(s * 0.72f, s * 0.2f), Size(s * 0.1f, s * 0.6f))
            drawPath(Path().apply { moveTo(s * 0.18f, s * 0.2f); lineTo(s * 0.18f, s * 0.8f); lineTo(s * 0.68f, s * 0.5f); close() }, Ink)
        }
    }
}

@Composable
private fun SongButton(label: String, onClick: () -> Unit, icon: androidx.compose.ui.graphics.drawscope.DrawScope.(Float) -> Unit) {
    Box(
        Modifier
            .size(56.dp)
            .clip(CircleShape)
            .border(2.dp, Ink, CircleShape)
            .clickable(role = Role.Button, onClickLabel = label, onClick = onClick)
            .semantics { contentDescription = label },
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.size(22.dp)) { icon(size.width) }
    }
}

@Composable
private fun Buttons(onJump: () -> Unit, onPin: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(24.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Pressable("Jump", filled = true, onClick = onJump, modifier = Modifier.weight(1f))
        Pressable("Pin", filled = false, onClick = onPin, modifier = Modifier.weight(1f))
    }
}

@Composable
private fun Pressable(label: String, filled: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val shape = RoundedCornerShape(8.dp)
    Box(
        modifier
            .height(72.dp)
            .clip(shape)
            .then(if (filled) Modifier.background(Ink) else Modifier.border(2.dp, Ink, shape))
            .clickable(role = Role.Button, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        BasicText(label, style = TextStyle(fontFamily = Barlow, fontWeight = FontWeight.Bold, fontSize = 28.sp, color = if (filled) Foam else Ink))
    }
}

@Composable
private fun Message(title: String, body: String, button: Pair<String, () -> Unit>?) {
    Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        BasicText(title, style = TextStyle(fontFamily = Barlow, fontWeight = FontWeight.Bold, fontSize = 34.sp, lineHeight = 36.sp, color = Ink))
        BasicText(body, style = TextStyle(fontFamily = Barlow, fontWeight = FontWeight.Medium, fontSize = 19.sp, color = Ink))
        button?.let { (label, onClick) -> Pressable(label, filled = true, onClick = onClick, modifier = Modifier.fillMaxWidth()) }
    }
}

private fun describe(chart: Chart, now: Long?): String = buildString {
    append("The song as a fishing line, ${clock(chart.durationMs)} long.")
    now?.let { append(" Playing at ${clock(it)}.") }
    chart.hookMs?.let { append(" ${chart.hookLabel} at ${clock(it)}.") }
    append(" ${chart.rewinds.size} rewinds and ${chart.skips.size} skips.")
}

/** What the hook's words say, kept to fade the old ones out during a cast or a reel. */
private data class HookWords(val title: String, val ms: Long?, val pinned: Boolean, val label: String, val detail: String) {
    companion object {
        fun of(chart: Chart) = HookWords(chart.title, chart.hookMs, chart.pinned, chart.hookLabel, chart.hookDetail)
    }
}

/** A cast of a new pin, or the reel of a removed one, and what was there before. */
private class HookChange(val cast: Boolean, val before: HookWords)
