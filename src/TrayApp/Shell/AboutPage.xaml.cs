using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TrayApp.Shell;

public partial class AboutPage
{
    public AboutPage()
    {
        InitializeComponent();
        // Image would take the first (16 px) frame of the .ico and blur it
        AppIcon.Source = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/app.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
            .Frames.MaxBy(f => f.PixelWidth);
        VersionText.Text = $"{Loc.T("About.Version")} {AppInfo.Version}";
        RepoLink.NavigateUri = $"https://github.com/{AppInfo.Repo}";
        IssuesLink.NavigateUri = $"https://github.com/{AppInfo.Repo}/issues";
        OpenDataButton.Click += (_, _) =>
        {
            if (Args.Current.Demo) return;
            Directory.CreateDirectory(AppInfo.DataDir);
            Process.Start("explorer.exe", $"\"{AppInfo.DataDir}\"");
        };
        RemoveButton.Click += (_, _) => Remove();
    }

    /// <summary>For a portable app this is the uninstall: data folders, Run value, shortcut, then exit. The exe stays.</summary>
    static void Remove()
    {
        if (MessageBox.Show(Loc.T("About.Remove.Confirm"), AppInfo.DisplayName, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        if (App.RemoveData?.Invoke() is { } reason)
        {
            MessageBox.Show(reason, AppInfo.DisplayName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (Args.Current.Demo) return;
        Autostart.Set(false);
        StartMenu.Set(false);
        App.Quit(confirm: false);
        Log.Off = true; // nothing may recreate the folders now
        foreach (var dir in new[] { AppInfo.DataDir, AppInfo.LocalDir })
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { } // a file held by another process: the user sees the folder and deletes it
    }
}
