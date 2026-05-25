using System;
using VideoEditor.Models;

namespace VideoEditor.Services;

public static class ProjectFormats
{
    public sealed record Preset(string Key, string Label, string ShortName,
                                int W, int H, string Mockup, string Description);

    public static readonly Preset[] All =
    {
        new("source",              "Source - match first clip",       "Source",   0,    0,    "source",   "Match the first clip"),
        new("yt_1080p",            "YouTube · 16:9",                   "YouTube",  1920, 1080, "monitor",  "Landscape video for desktop / TV"),
        new("reels_1080",          "Reels / TikTok / Shorts · 9:16",   "Reels",    1080, 1920, "phone",    "Vertical video for phones"),
        new("square_1080",         "Instagram Square · 1:1",           "Square",   1080, 1080, "square",   "Square feed post"),
        new("portrait_1080x1350",  "Instagram Portrait · 4:5",         "Portrait", 1080, 1350, "portrait", "Tall feed post"),
        new("cinematic_2560x1080", "Cinematic · 21:9",                 "Cinema",   2560, 1080, "cinema",   "Ultrawide cinematic letterbox"),
        new("custom",              "Custom W×H",                       "Custom",   0,    0,    "custom",   "Pick any width and height"),
    };

    public static (int w, int h) Resolve(string? key, VideoClip? firstVideo)
    {
        key ??= "source";

        if (key == "custom")
            return (Even(AppSettings.CustomTargetWidth), Even(AppSettings.CustomTargetHeight));

        if (key == "source")
        {
            int w = firstVideo?.VideoWidth ?? 0;
            int h = firstVideo?.VideoHeight ?? 0;
            if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
            return (Even(w), Even(h));
        }

        var p = Array.Find(All, x => x.Key == key);
        if (p == null) return (1920, 1080);
        return (Even(p.W), Even(p.H));
    }

    public static Preset Lookup(string? key) =>
        Array.Find(All, x => x.Key == key) ?? All[0];

    private static int Even(int n) => n <= 0 ? 0 : (n % 2 == 0 ? n : n + 1);
}
