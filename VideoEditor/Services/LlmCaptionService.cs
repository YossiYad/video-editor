using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoEditor.Models;

namespace VideoEditor.Services;

/// <summary>
/// Glue between local Whisper transcription and Google Gemini.
/// Hands the model the spoken segments and asks for short, punchy on-screen
/// captions ("kinetic typography") with timing + colour, then maps each
/// returned item into a <see cref="TextOverlay"/> the timeline can render.
/// </summary>
public sealed class LlmCaptionService
{
    /// <summary>
    /// Models we will try, in preference order. Free-tier availability per
    /// model varies wildly per Google project — newer keys often have 0 quota
    /// on gemini-2.0-flash but plenty on 2.5-flash, and vice versa. We probe
    /// the list and remember the first one that answers 200.
    /// </summary>
    private static readonly string[] FallbackModels =
    {
        "gemini-2.5-flash",
        "gemini-2.0-flash",
        "gemini-2.5-flash-lite",
        "gemini-1.5-flash-latest",
        "gemini-1.5-flash",
    };

    private static string EndpointFor(string model) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>The model the most recent PingAsync / GenerateOverlaysAsync settled on.</summary>
    public string? LastUsedModel { get; private set; }

    public event Action<string>? Log;

    /// <summary>
    /// Sanity-check an API key with a tiny round-trip. Tries each model in
    /// <see cref="FallbackModels"/> and returns true on the first 200 OK.
    /// On failure, throws with a combined error message so the user can see
    /// which models were tried.
    /// </summary>
    public async Task<bool> PingAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = "Reply with the single word: OK" } } }
            },
            generationConfig = new { temperature = 0.0 }
        };
        var json = JsonSerializer.Serialize(body);

        var perModelErrors = new List<string>();
        foreach (var model in FallbackModels)
        {
            ct.ThrowIfCancellationRequested();
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{EndpointFor(model)}?key={Uri.EscapeDataString(apiKey)}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var resp = await Http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            Log?.Invoke($"Gemini ping ({model}) → {(int)resp.StatusCode}");
            if (resp.IsSuccessStatusCode)
            {
                var replyText = ExtractFirstText(text);
                if (!string.IsNullOrEmpty(replyText) &&
                    replyText.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LastUsedModel = model;
                    AppSettings.LlmModel = model;
                    AppSettings.Save();
                    return true;
                }
            }
            else
            {
                var msg = ExtractErrorMessage(text) ?? $"HTTP {(int)resp.StatusCode}";
                perModelErrors.Add($"{model}: {Shorten(msg)}");
            }
        }
        throw new Exception("All Gemini models rejected the key:\n - " + string.Join("\n - ", perModelErrors));
    }

    private static string Shorten(string s)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length > 200 ? s.Substring(0, 200) + "…" : s;
    }

    public async Task<List<TextOverlay>> GenerateOverlaysAsync(
        IList<SubtitleSegment> segments,
        int videoWidth, int videoHeight,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var apiKey = AppSettings.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is missing. Set it in Settings → AI Captions.");
        if (segments == null || segments.Count == 0)
            return new List<TextOverlay>();

        progress?.Report(0.05);
        var prompt = BuildPrompt(segments);
        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = 0.6
            }
        };
        var json = JsonSerializer.Serialize(body);

        // Probe order: prefer the model the user already validated in Settings
        // (Test connection writes it to AppSettings.LlmModel), then the rest.
        var probeOrder = new List<string>();
        if (!string.IsNullOrEmpty(AppSettings.LlmModel)) probeOrder.Add(AppSettings.LlmModel);
        foreach (var m in FallbackModels)
            if (!probeOrder.Contains(m, StringComparer.OrdinalIgnoreCase)) probeOrder.Add(m);

        progress?.Report(0.2);

        var perModelErrors = new List<string>();
        string? replyText = null;
        foreach (var model in probeOrder)
        {
            ct.ThrowIfCancellationRequested();
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{EndpointFor(model)}?key={Uri.EscapeDataString(apiKey)}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var resp = await Http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            Log?.Invoke($"Gemini generate ({model}) → {(int)resp.StatusCode}");
            if (resp.IsSuccessStatusCode)
            {
                replyText = ExtractFirstText(text);
                LastUsedModel = model;
                AppSettings.LlmModel = model;
                AppSettings.Save();
                break;
            }
            var msg = ExtractErrorMessage(text) ?? $"HTTP {(int)resp.StatusCode}";
            perModelErrors.Add($"{model}: {Shorten(msg)}");
        }
        if (replyText == null)
            throw new Exception("All Gemini models refused the request:\n - " + string.Join("\n - ", perModelErrors));

        progress?.Report(0.75);
        var reply = replyText ?? "";
        var items = ParseLlmCaptions(reply);
        if (items.Count == 0)
            throw new Exception("Gemini returned no caption items. Try again or change the source.");

        double lastEnd = 0;
        foreach (var s in segments) lastEnd = Math.Max(lastEnd, s.EndSeconds);
        var overlays = MapToOverlays(items, videoWidth, videoHeight, lastEnd);
        progress?.Report(1.0);
        return overlays;
    }

    // ---------- prompt ----------

    private static string BuildPrompt(IList<SubtitleSegment> segments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert short-form video editor (Reels / TikTok / Shorts).");
        sb.AppendLine("Convert the transcript below into a sequence of short, punchy on-screen captions");
        sb.AppendLine("that drive engagement — \"kinetic typography\" style.");
        sb.AppendLine();
        sb.AppendLine("Rules (must follow):");
        sb.AppendLine("- Output STRICT JSON: an array of objects with exactly these keys: start, end, text, color.");
        sb.AppendLine("- Times are seconds (number) measured from 0.");
        sb.AppendLine("- Caption ranges must NOT overlap and must be in chronological order.");
        sb.AppendLine("- Cover roughly the whole spoken duration; gaps between captions are fine.");
        sb.AppendLine("- text: 3–7 words per caption (1–5 in Hebrew). Use the SAME language as the transcript — do not translate.");
        sb.AppendLine("- Capitalise for emphasis sparingly. Avoid ALL CAPS on every single line.");
        sb.AppendLine("- color: pick from this small palette to stay consistent — \"white\", \"yellow\", \"cyan\", \"red\".");
        sb.AppendLine("  Use \"white\" most of the time; switch colour only for emphasis on key words or numbers.");
        sb.AppendLine("- Do NOT wrap the JSON in markdown / code fences. Return raw JSON only.");
        sb.AppendLine();
        sb.AppendLine("Transcript segments (JSON):");
        sb.Append('[');
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (i > 0) sb.Append(',');
            sb.Append('{');
            sb.Append("\"start\":").Append(s.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"end\":").Append(s.EndSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"text\":").Append(JsonSerializer.Serialize(s.Text ?? ""));
            sb.Append('}');
        }
        sb.AppendLine("]");
        sb.AppendLine();
        sb.AppendLine("Return only the JSON array, no commentary.");
        return sb.ToString();
    }

    // ---------- parsing ----------

    private sealed class LlmCaption
    {
        public double Start { get; set; }
        public double End { get; set; }
        public string Text { get; set; } = "";
        public string? Color { get; set; }
    }

    private static List<LlmCaption> ParseLlmCaptions(string raw)
    {
        var result = new List<LlmCaption>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var trimmed = raw.Trim();
        int firstBracket = trimmed.IndexOf('[');
        int lastBracket = trimmed.LastIndexOf(']');
        if (firstBracket < 0 || lastBracket <= firstBracket) return result;
        trimmed = trimmed.Substring(firstBracket, lastBracket - firstBracket + 1);

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var item = new LlmCaption();
                if (el.TryGetProperty("start", out var s) && s.ValueKind == JsonValueKind.Number)
                    item.Start = s.GetDouble();
                if (el.TryGetProperty("end", out var e) && e.ValueKind == JsonValueKind.Number)
                    item.End = e.GetDouble();
                if (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    item.Text = t.GetString() ?? "";
                if (el.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String)
                    item.Color = c.GetString();
                if (!string.IsNullOrWhiteSpace(item.Text) && item.End > item.Start)
                    result.Add(item);
            }
        }
        catch { /* fall through – an empty list signals failure to the caller */ }
        return result;
    }

    private static List<TextOverlay> MapToOverlays(
        List<LlmCaption> items, int videoWidth, int videoHeight, double maxEnd)
    {
        var overlays = new List<TextOverlay>(items.Count);
        var fontSize = Math.Max(28, videoHeight / 16);
        var lowerThirdY = Math.Max(0, videoHeight - videoHeight / 5);

        double last = 0;
        foreach (var it in items)
        {
            var start = Math.Max(0, it.Start);
            if (start < last) start = last;
            var end = Math.Min(maxEnd > 0 ? maxEnd : it.End, it.End);
            if (end <= start) continue;

            var text = (it.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) continue;

            var color = NormaliseColor(it.Color);
            var halfWidth = EstimateHalfWidth(text, fontSize);
            var x = Math.Max(8, videoWidth / 2 - halfWidth);

            overlays.Add(new TextOverlay
            {
                Text = text,
                X = x,
                Y = lowerThirdY,
                FontSize = fontSize,
                FontColor = color,
                Bold = true,
                Italic = false,
                BackgroundEnabled = true,
                BackgroundColor = "black",
                BackgroundOpacity = 0.55,
                BackgroundPadding = 14,
                StartSeconds = start,
                EndSeconds = end
            });
            last = end;
        }
        return overlays;
    }

    private static string NormaliseColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return "white";
        var c = color.Trim().ToLowerInvariant();
        return c switch
        {
            "white" or "yellow" or "cyan" or "red" => c,
            _ => "white"
        };
    }

    private static int EstimateHalfWidth(string text, int fontSize)
    {
        // Rough but stable: 0.55em per glyph for bold sans-serif → halve for centring.
        var approx = (int)(text.Length * fontSize * 0.55);
        return Math.Max(20, approx / 2);
    }

    // ---------- response shape helpers ----------

    /// <summary>Pull the first text part out of the standard Gemini response shape.</summary>
    private static string? ExtractFirstText(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("candidates", out var cands) ||
                cands.ValueKind != JsonValueKind.Array) return null;
            foreach (var cand in cands.EnumerateArray())
            {
                if (!cand.TryGetProperty("content", out var content)) continue;
                if (!content.TryGetProperty("parts", out var parts) ||
                    parts.ValueKind != JsonValueKind.Array) continue;
                var sb = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
                }
                if (sb.Length > 0) return sb.ToString();
            }
        }
        catch { }
        return null;
    }

    private static string? ExtractErrorMessage(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    return m.GetString();
            }
        }
        catch { }
        return null;
    }
}
