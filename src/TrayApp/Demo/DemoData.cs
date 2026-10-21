using TrayApp.Features;
using TrayApp.Shell;

namespace TrayApp.Demo;

/// <summary>
/// --demo: believable data for screenshots and GIFs. Names like Alex, example.com, C:\Users\demo\…; never real data.
/// Under --demo no real services start, nothing is written, no hotkeys, no update check (00-shared §2.3).
/// </summary>
public static class DemoData
{
    public static void Fill()
    {
        MainPage.Items.Add(new(Loc.T("Demo.Build"), @"C:\Users\demo\src\shop", true));
        MainPage.Items.Add(new(Loc.T("Demo.Dataset"), "https://example.com/data.zip", true));
        MainPage.Items.Add(new(Loc.T("Demo.Report"), Loc.T("Demo.Daily"), false));
    }
}
