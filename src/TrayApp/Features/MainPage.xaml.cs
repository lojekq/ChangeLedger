using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace TrayApp.Features;

public sealed class SampleItem(string name, string detail, bool on) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public string Detail { get; } = detail;

    public bool On
    {
        get => on;
        set { on = value; Changed(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}

public partial class MainPage
{
    public static ObservableCollection<SampleItem> Items { get; } = [];

    public MainPage()
    {
        InitializeComponent();
        void Sync() => EmptyText.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Items.CollectionChanged += (_, _) => Sync();
        Sync();
    }
}
