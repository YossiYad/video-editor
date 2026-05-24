using System;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VideoEditor.Views;

public class TextToSpeechWindow : Window
{
    public TextToSpeechWindow()
    {
        Title = "Text to Speech";
        var ch = WindowBuilder.Build(this, "🎙", "Text to Speech",
            "Generate a WAV from text using the system voice", 560, 460);

        ch.Body.Children.Add(WindowBuilder.Lbl("Text"));
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        ch.Body.Children.Add(box);

        ch.Body.Children.Add(WindowBuilder.Lbl("Voice"));
        var voiceBox = new ComboBox();
        try
        {
            using var s = new SpeechSynthesizer();
            foreach (var v in s.GetInstalledVoices()) voiceBox.Items.Add(v.VoiceInfo.Name);
            if (voiceBox.Items.Count > 0) voiceBox.SelectedIndex = 0;
        }
        catch { }
        ch.Body.Children.Add(voiceBox);

        ch.Body.Children.Add(WindowBuilder.Lbl("Rate"));
        var rate = new Slider { Minimum = -10, Maximum = 10, Value = 0 };
        ch.Body.Children.Add(rate);

        ch.Primary.Content = "Save WAV";
        ch.Primary.Click += (_, _) =>
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
