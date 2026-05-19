using System;
using System.IO;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace VideoEditor.Views;

public class TextToSpeechWindow : Window
{
    public TextToSpeechWindow()
    {
        Title = "Text to Speech"; Width = 520; Height = 420;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Text"));
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(box);
        body.Children.Add(WindowBuilder.Lbl("Voice"));
        var voiceBox = new ComboBox();
        try
        {
            using var s = new SpeechSynthesizer();
            foreach (var v in s.GetInstalledVoices()) voiceBox.Items.Add(v.VoiceInfo.Name);
            if (voiceBox.Items.Count > 0) voiceBox.SelectedIndex = 0;
        }
        catch { }
        body.Children.Add(voiceBox);

        body.Children.Add(WindowBuilder.Lbl("Rate"));
        var rate = new Slider { Minimum = -10, Maximum = 10, Value = 0 };
        body.Children.Add(rate);

        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Content = "Save WAV";
        ok.Click += (_, _) =>
        {
            try
            {
                var sfd = new SaveFileDialog { FileName = "tts.wav", Filter = "WAV|*.wav" };
                if (sfd.ShowDialog() != true) return;
                using var synth = new SpeechSynthesizer();
                if (voiceBox.SelectedItem is string v) synth.SelectVoice(v);
                synth.Rate = (int)rate.Value;
                synth.SetOutputToWaveFile(sfd.FileName);
                synth.Speak(box.Text);
                MessageBox.Show("Saved: " + sfd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("TTS failed: " + ex.Message);
            }
        };
    }
}
