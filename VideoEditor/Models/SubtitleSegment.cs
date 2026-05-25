namespace VideoEditor.Models;

public class SubtitleSegment
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Text { get; set; } = "";
}
