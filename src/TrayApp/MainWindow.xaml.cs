using TrayApp.Shell;

namespace TrayApp;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ElevatedBar.IsOpen = AppInfo.IsElevated;
        Loaded += (_, _) => { if (Nav.SelectedItem is null) Navigate("main"); };
    }

    /// <summary>By TargetPageTag: "main", "settings", "about", app pages.</summary>
    public void Navigate(string page) => Nav.Navigate(page);

    public void ShowUpdate(string version, string url)
    {
        UpdateBar.Message = string.Format(Loc.T("Update.Message"), version);
        UpdateLink.NavigateUri = url;
        UpdateBar.IsOpen = true;
    }
}
