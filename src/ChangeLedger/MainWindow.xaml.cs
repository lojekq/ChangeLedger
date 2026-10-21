using System.Windows;
using System.Windows.Controls;
using ChangeLedger.Shell;

namespace ChangeLedger;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ElevatedBar.IsOpen = AppInfo.IsElevated;
        // WPF-UI wraps every Page in its own DynamicScrollViewer unless the page says ScrollViewer.CanContentScroll="False".
        // Our pages scroll themselves (a ScrollViewer, or a list in a * row): inside that wrapper they get infinite height,
        // never scroll, and their ScrollViewer still marks the mouse wheel handled, so nothing scrolls at all.
        Nav.Navigating += (_, e) => { if (e.Page is DependencyObject page) ScrollViewer.SetCanContentScroll(page, false); };
        Loaded += (_, _) => { if (Nav.SelectedItem is null) Navigate("changes"); };
    }

    /// <summary>By TargetPageTag: "changes", "power", "settings", "about".</summary>
    public void Navigate(string page) => Nav.Navigate(page);

    public void ShowUpdate(string version, string url)
    {
        UpdateBar.Message = string.Format(Loc.T("Update.Message"), version);
        UpdateLink.NavigateUri = url;
        UpdateBar.IsOpen = true;
    }
}
