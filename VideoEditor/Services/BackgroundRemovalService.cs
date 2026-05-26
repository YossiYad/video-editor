using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VideoEditor.Models;

namespace VideoEditor.Services;

/// <summary>
/// Free, fully-local "virtual background" for camera recordings. Runs the MODNet
/// portrait-matting model (ONNX, CPU) to separate the person from the background,
/// then composites the chosen replacement (blur / transparent / colour / image).
///
/// The model (~25 MB) auto-downloads on first use, mirroring how the app fetches
/// FFmpeg and the Whisper models. Nothing leaves the machine; there is no API key
/// and no network access during recording.
/// </summary>
public sealed class BackgroundRemovalService : IDisposable
{
    private static readonly string ModelFolder =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
    private static readonly string ModelFile =
        Path.Combine(ModelFolder, "modnet_portrait_matting.onnx");

    // Official, stable OpenVINO Open Model Zoo mirror of the MODNet ONNX export.
    private const string ModelUrl =
        "https://storage.openvinotoolkit.org/repositories/open_model_zoo/public/2022.2/modnet-photographic-portrait-matting/modnet_photographic_portrait_matting.onnx";

    // MODNet expects a square RGB input normalised to [-1, 1] and returns a single
    // alpha channel at the same resolution.
    private const int ModelSize = 512;

    public event Action<string>? Log;

    private readonly object _sessionLock = new();
    private InferenceSession? _session;
    private string _inputName = "input";
    private string _outputName = "output";

    // Background image is decoded + cover-fitted once and cached for the frame size.
    private byte[]? _bgImageCache;
    private int _bgImageW, _bgImageH;
    private string? _bgImagePath;

    public static bool IsModelReady() => File.Exists(ModelFile);

    /// <summary>
    /// Load the ONNX session and run a tiny dummy inference so the first real
    /// frame doesn't pay the multi-second cold-start cost. Safe to call once
    /// the model file exists; cheap to call again afterwards.
    /// </summary>
    public Task WarmUpAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        if (!File.Exists(ModelFile)) return;
        try
        {
            var session = GetSession();
            var input = new DenseTensor<float>(new[] { 1, 3, ModelSize, ModelSize });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, input) };
            lock (_sessionLock)
            {
                using var _ = session.Run(inputs);
            }
        }
        catch { /* warm-up is best-effort */ }
    }, ct);

    public async Task EnsureModelAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (File.Exists(ModelFile)) return;
        Directory.CreateDirectory(ModelFolder);
        Log?.Invoke("Downloading AI background model (~25 MB) - first run only...");

        var tmp = ModelFile + ".part";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoEditor/1.0");
            using var resp = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long? total = resp.Content.Headers.ContentLength;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[81920];
                long got = 0;
                int read;
                while ((read = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct);
                    got += read;
                    if (total.HasValue) progress?.Report((double)got / total.Value);
                }
            }
            File.Move(tmp, ModelFile, overwrite: true);
            progress?.Report(1.0);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private InferenceSession GetSession()
    {
        lock (_sessionLock)
        {
            if (_session == null)
            {
                if (!File.Exists(ModelFile))
                    throw new FileNotFoundException("AI background model has not been downloaded yet.", ModelFile);
                _session = new InferenceSession(ModelFile);
                // Don't assume the exported names - read them from the graph so a
                // re-export with different names still works.
                _inputName = _session.InputMetadata.Keys.First();
                _outputName = _session.OutputMetadata.Keys.First();
            }
            return _session;
        }
    }

    // ---- Public per-frame entry points -------------------------------------

    /// <summary>Apply the chosen background to one BGRA frame, returning a new BGRA buffer.</summary>
    public byte[] ProcessFrameBytes(byte[] bgra, int w, int h, CameraBackground bg)
    {
        if (bg.Mode == CameraBackgroundMode.None) return bgra;
        var alpha = InferAlpha(bgra, w, h);
        return Composite(bgra, w, h, bg, alpha);
    }

    /// <summary>Convenience wrapper for the live preview, working on a Bitmap.</summary>
    public Bitmap ProcessBitmap(Bitmap src, CameraBackground bg)
    {
        int w = src.Width, h = src.Height;
        var bgra = BitmapToBgra(src);
        var outBgra = ProcessFrameBytes(bgra, w, h, bg);
        return BgraToBitmap(outBgra, w, h);
    }

    // ---- Inference ---------------------------------------------------------

    private float[] InferAlpha(byte[] bgra, int w, int h)
    {
        var session = GetSession();

        // Downscale the frame to the model's square input using GDI+ (well-tested,
        // avoids a hand-rolled resampler), then fill the normalised RGB tensor.
        byte[] small;
        using (var full = BgraToBitmap(bgra, w, h))
        using (var resized = ResizeBitmap(full, ModelSize, ModelSize))
            small = BitmapToBgra(resized);

        // Fill the normalised RGB tensor by writing straight to its backing buffer
        // (the per-element multidimensional indexer allocates and is far too slow at
        // 512*512*3 writes per frame). Layout for [1,3,H,W] is plane-major: c*H*W + y*W + x.
        var input = new DenseTensor<float>(new[] { 1, 3, ModelSize, ModelSize });
        var tbuf = input.Buffer.Span;
        int plane = ModelSize * ModelSize;
        for (int y = 0; y < ModelSize; y++)
        {
            int row = y * ModelSize * 4;
            int outRow = y * ModelSize;
            for (int x = 0; x < ModelSize; x++)
            {
                int i = row + x * 4;
                int o = outRow + x;
                tbuf[o] = (small[i + 2] - 127.5f) / 127.5f;             // R
                tbuf[plane + o] = (small[i + 1] - 127.5f) / 127.5f;     // G
                tbuf[2 * plane + o] = (small[i] - 127.5f) / 127.5f;     // B
            }
        }

        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, input) };
        float[] maskSmall;
        lock (_sessionLock)
        {
            using var results = session.Run(inputs);
            maskSmall = results.First(r => r.Name == _outputName).AsTensor<float>().ToArray();
        }
        for (int j = 0; j < maskSmall.Length; j++)
            maskSmall[j] = Math.Clamp(maskSmall[j], 0f, 1f);

        // Upsample the mask back to the frame size, again via GDI+.
        var maskBytes = new byte[ModelSize * ModelSize * 4];
        for (int j = 0; j < maskSmall.Length; j++)
        {
            byte v = (byte)(maskSmall[j] * 255f);
            int i = j * 4;
            maskBytes[i] = v; maskBytes[i + 1] = v; maskBytes[i + 2] = v; maskBytes[i + 3] = 255;
        }

        var alpha = new float[w * h];
        using (var mask512 = BgraToBitmap(maskBytes, ModelSize, ModelSize))
        using (var maskFull = ResizeBitmap(mask512, w, h))
        {
            var md = BitmapToBgra(maskFull);
            for (int j = 0; j < alpha.Length; j++)
                alpha[j] = md[j * 4] / 255f;
        }
        return alpha;
    }

    // ---- Compositing -------------------------------------------------------

    private byte[] Composite(byte[] fg, int w, int h, CameraBackground bg, float[] alpha)
    {
        var outBuf = new byte[w * h * 4];

        if (bg.Mode == CameraBackgroundMode.Transparent)
        {
            for (int p = 0; p < w * h; p++)
            {
                int i = p * 4;
                outBuf[i] = fg[i];
                outBuf[i + 1] = fg[i + 1];
                outBuf[i + 2] = fg[i + 2];
                outBuf[i + 3] = (byte)(Math.Clamp(alpha[p], 0f, 1f) * 255f);
            }
            return outBuf;
        }

        byte[]? back = bg.Mode switch
        {
            CameraBackgroundMode.Blur => BlurBgra(fg, w, h),
            CameraBackgroundMode.Image => LoadBackgroundCover(bg.ImagePath, w, h),
            _ => null
        };

        for (int p = 0; p < w * h; p++)
        {
            int i = p * 4;
            float a = Math.Clamp(alpha[p], 0f, 1f);
            float inv = 1f - a;

            byte bb, bg_, br;
            if (back != null)
            {
                bb = back[i]; bg_ = back[i + 1]; br = back[i + 2];
            }
            else // solid colour (also the fallback if an image failed to load)
            {
                bb = bg.ColorB; bg_ = bg.ColorG; br = bg.ColorR;
            }

            outBuf[i] = (byte)(fg[i] * a + bb * inv);
            outBuf[i + 1] = (byte)(fg[i + 1] * a + bg_ * inv);
            outBuf[i + 2] = (byte)(fg[i + 2] * a + br * inv);
            outBuf[i + 3] = 255;
        }
        return outBuf;
    }

    /// <summary>Cheap soft blur: shrink hard, scale back up with bilinear filtering.</summary>
    private static byte[] BlurBgra(byte[] bgra, int w, int h)
    {
        int sw = Math.Max(1, w / 14);
        int sh = Math.Max(1, h / 14);
        using var full = BgraToBitmap(bgra, w, h);
        using var small = ResizeBitmap(full, sw, sh);
        using var back = ResizeBitmap(small, w, h);
        return BitmapToBgra(back);
    }

    /// <summary>Load + cover-fit the still background, caching the scaled BGRA buffer.</summary>
    private byte[]? LoadBackgroundCover(string? path, int w, int h)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (_bgImageCache != null && _bgImageW == w && _bgImageH == h && _bgImagePath == path)
            return _bgImageCache;

        try
        {
            using var srcImg = new Bitmap(path);
            using var canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(canvas))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                // Cover-fit: scale to fill, centre-crop the overflow.
                double scale = Math.Max((double)w / srcImg.Width, (double)h / srcImg.Height);
                int dw = (int)Math.Ceiling(srcImg.Width * scale);
                int dh = (int)Math.Ceiling(srcImg.Height * scale);
                int dx = (w - dw) / 2;
                int dy = (h - dh) / 2;
                g.DrawImage(srcImg, dx, dy, dw, dh);
            }
            _bgImageCache = BitmapToBgra(canvas);
            _bgImageW = w; _bgImageH = h; _bgImagePath = path;
            return _bgImageCache;
        }
        catch
        {
            return null;
        }
    }

    // ---- Whole-file processing (post-process a finished recording) ----------

    /// <summary>
    /// Re-render a recorded camera clip with the chosen background. Decodes the
    /// source to raw BGRA via FFmpeg, composites every frame in-process, and pipes
    /// the result into a second FFmpeg that encodes the final file. Opaque modes
    /// produce H.264 MP4; Transparent produces VP9/WebM with a real alpha channel.
    /// </summary>
    public async Task ProcessVideoAsync(
        FFmpegService ff, string inputPath, string outputPath,
        CameraBackground bg, int fps,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var (w, h, dur) = await ff.ProbeAsync(inputPath);
        if (w <= 0 || h <= 0) throw new Exception("Could not read the recorded video dimensions.");

        // libx264 / yuv420p need even dimensions - crop a stray odd row/column.
        int ew = w - (w % 2);
        int eh = h - (h % 2);
        long frameBytes = (long)ew * eh * 4;
        long totalFrames = (dur > 0 && fps > 0) ? (long)(dur * fps) : 0;

        var inArgs = $"-y -i \"{inputPath}\" -vf crop={ew}:{eh}:0:0 -f rawvideo -pix_fmt bgra -";
        var outArgs = bg.NeedsAlpha
            ? $"-y -f rawvideo -pix_fmt bgra -s {ew}x{eh} -framerate {fps} -i - " +
              $"-c:v libvpx-vp9 -pix_fmt yuva420p -b:v 0 -crf 28 \"{outputPath}\""
            : $"-y -f rawvideo -pix_fmt bgra -s {ew}x{eh} -framerate {fps} -i - " +
              $"-c:v libx264 -preset veryfast -pix_fmt yuv420p \"{outputPath}\"";

        using var inProc = StartFfmpeg(ff.FFmpegExe, inArgs, redirectStdout: true);
        using var outProc = StartFfmpeg(ff.FFmpegExe, outArgs, redirectStdin: true);

        // Drain both stderr streams so neither process blocks on a full pipe.
        DrainStdErr(inProc);
        DrainStdErr(outProc);

        var srcStream = inProc.StandardOutput.BaseStream;
        var dstStream = outProc.StandardInput.BaseStream;
        var frame = new byte[frameBytes];
        long done = 0;

        try
        {
            while (!ct.IsCancellationRequested && await ReadFullAsync(srcStream, frame, ct))
            {
                var processed = ProcessFrameBytes(frame, ew, eh, bg);
                await dstStream.WriteAsync(processed.AsMemory(0, (int)frameBytes), ct);
                done++;
                if (totalFrames > 0) progress?.Report(Math.Min(0.999, (double)done / totalFrames));
            }
            await dstStream.FlushAsync(ct);
        }
        finally
        {
            try { outProc.StandardInput.Close(); } catch { }
        }

        await outProc.WaitForExitAsync(ct);
        try { if (!inProc.HasExited) inProc.Kill(); } catch { }
        progress?.Report(1.0);

        if (outProc.ExitCode != 0)
            throw new Exception($"Background render failed (ffmpeg exit {outProc.ExitCode}).");
    }

    private static Process StartFfmpeg(string exe, string args, bool redirectStdout = false, bool redirectStdin = false)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = redirectStdout,
                RedirectStandardInput = redirectStdin,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };
        p.Start();
        return p;
    }

    private void DrainStdErr(Process p)
    {
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Log?.Invoke(e.Data); };
        p.BeginErrorReadLine();
    }

    private static async Task<bool> ReadFullAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(off, buf.Length - off), ct);
            if (n == 0) return false; // clean EOF
            off += n;
        }
        return true;
    }

    // ---- BGRA <-> Bitmap helpers (all 32bpp, stride == 4*width) ------------

    private static Bitmap ResizeBitmap(Bitmap src, int w, int h)
    {
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(src, 0, 0, w, h);
        return dst;
    }

    private static byte[] BitmapToBgra(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var buf = new byte[w * h * 4];
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride == w * 4)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            }
            else
            {
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + y * data.Stride, buf, y * w * 4, w * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
        return buf;
    }

    private static Bitmap BgraToBitmap(byte[] bgra, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride == w * 4)
            {
                System.Runtime.InteropServices.Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
            }
            else
            {
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
