using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class MergeVideosWindow : Window
{
    private readonly FFmpegService _ff;
    public MergeVideosWindow(FFmpegService ff)
    {
        _ff = ff;
        Title = "Merge Videos";
        var ch = WindowBuilder.Build(this, "🔗", "Merge Videos",
            "Concatenate clips in the order shown", 600, 500);

        ch.Body.Children.Add(WindowBuilder.Lbl("Videos to merge (in order)"));

        var list = new ListBox
        {
            Height = 240,
            Background = WindowBuilder.Bg1,
            Foreground = WindowBuilder.TextBr,
            BorderBrush = WindowBuilder.Line,
            BorderThickness = new Thickness(1)
        };
        ch.Body.Children.Add(list);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        Button MakeB(string text) { var b = new Button { Content = text, MinWidth = 64, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) }; b.Style = (Style)FindResource("ToolButton"); return b; }
        var add  = MakeB("➕ Add");
        var rem  = MakeB("➖ Remove");
        var up   = MakeB("↑");
        var down = MakeB("↓");
        btns.Children.Add(add); btns.Children.Add(rem); btns.Children.Add(up); btns.Children.Add(down);
        ch.Body.Children.Add(btns);

        add.Click += (_, _) =>
        {
            var d = new OpenFileDialog { Multiselect = true, Filter = "Video|*.mp4;*.mov;*.mkv;*.avi;*.webm|All|*.*" };
            if (d.ShowDialog() == true)
                foreach (var f in d.FileNames) list.Items.Add(f);
        };
        rem.Click += (_, _) => { if (list.SelectedItem != null) list.Items.Remove(list.SelectedItem); };
        up.Click += (_, _) =>
        {
            var i = list.SelectedIndex;
            if (i > 0) { var item = list.SelectedItem; list.Items.RemoveAt(i); list.Items.Insert(i - 1, item); list.SelectedIndex = i - 1; }
        };
        down.Click += (_, _) =>
        {
            var i = list.SelectedIndex;
            if (i >= 0 && i < list.Items.Count - 1) { var item = list.SelectedItem; list.Items.RemoveAt(i); list.Items.Insert(i + 1, item); list.SelectedIndex = i + 1; }
        };

        ch.Primary.Content = "Merge & Save";
        ch.Primary.Click += async (_, _) =>
        {
            if (list.Items.Count < 2) { MessageBox.Show("Add at least 2 videos."); return; }
            var sfd = new SaveFileDialog { FileName = "merged.mp4", Filter = "MP4|*.mp4" };
            if (sfd.ShowDialog() != true) return;
            try
            {
                var files = list.Items.Cast<string>().ToList();
                await _ff.MergeAsync(files, sfd.FileName);
                MessageBox.Show("Saved: " + sfd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Merge failed: " + ex.Message);
            }
        };
    }
}
