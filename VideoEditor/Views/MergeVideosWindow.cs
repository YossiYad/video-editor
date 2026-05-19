using System;
using System.Collections.ObjectModel;
using System.IO;
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
        Title = "Merge Videos"; Width = 600; Height = 460;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Videos to merge (in order)"));

        var list = new ListBox { Height = 240, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3E)), Foreground = Brushes.White };
        body.Children.Add(list);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var add = new Button { Content = "Add", Width = 80, Margin = new Thickness(2), Style = (Style)FindResource("ToolButton") };
        var rem = new Button { Content = "Remove", Width = 80, Margin = new Thickness(2), Style = (Style)FindResource("ToolButton") };
        var up = new Button { Content = "↑", Width = 40, Margin = new Thickness(2), Style = (Style)FindResource("ToolButton") };
        var down = new Button { Content = "↓", Width = 40, Margin = new Thickness(2), Style = (Style)FindResource("ToolButton") };
        btns.Children.Add(add); btns.Children.Add(rem); btns.Children.Add(up); btns.Children.Add(down);
        body.Children.Add(btns);

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

        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Content = "Merge & Save";
        ok.Click += async (_, _) =>
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
