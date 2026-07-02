using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class BrowserLoginRequiredDialog : Window
{
    private readonly string _url;
    private readonly WindowBuilder.Chrome _chrome;
    private readonly CheckBox _rememberMe;
    private bool _openedBrowser;

    public bool RememberMe => _rememberMe.IsChecked == true;

    public BrowserLoginRequiredDialog(LoginRequiredDownloadException error)
    {
        _url = error.Url;
        _chrome = WindowBuilder.Build(
            this,
            "!",
            $"{error.SiteName} requires login",
            "Open the site in your browser, sign in, then try the download again.",
            520,
            330,
            "Open login");

        _chrome.Body.Children.Add(new TextBlock
        {
            Text = $"The video is not public to anonymous downloaders. Sign in to {error.SiteName} in your browser, make sure the video plays there, then return here.",
            Foreground = WindowBuilder.TextMute,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        if (!string.IsNullOrWhiteSpace(error.InnerException?.Message))
        {
            _chrome.Body.Children.Add(new TextBlock
            {
                Text = error.InnerException.Message,
                Foreground = WindowBuilder.TextDim,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                MaxHeight = 62
            });
        }

        var urlCard = new Border
        {
            Background = WindowBuilder.Bg2,
            BorderBrush = WindowBuilder.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        urlCard.Child = new TextBlock
        {
            Text = _url,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = WindowBuilder.TextDim,
            TextWrapping = TextWrapping.Wrap
        };
        _chrome.Body.Children.Add(urlCard);

        _chrome.Body.Children.Add(new TextBlock
        {
            Text = "If the video still does not play in your browser after login, the app cannot download it either.",
            Foreground = WindowBuilder.TextDim,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });

        _rememberMe = new CheckBox
        {
            Content = "Remember me",
            Foreground = WindowBuilder.TextMute,
            FontSize = 12,
            Margin = new Thickness(0, 14, 0, 0),
            IsChecked = AppSettings.DownloadRememberBrowserLogin
        };
        _chrome.Body.Children.Add(_rememberMe);

        _chrome.Primary.Click += (_, _) => Primary_Click();
    }

    private void Primary_Click()
    {
        if (!_openedBrowser)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't open the browser: " + ex.Message, "Open Login");
                return;
            }

            _openedBrowser = true;
            _chrome.Primary.Content = "Try again";
            return;
        }

        DialogResult = true;
        Close();
    }
}
