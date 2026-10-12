using System;
using AnimeTracker.ViewModels;
using Avalonia.Controls;

namespace AnimeTracker.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenAddTitleRequested += async () =>
            {
                var addVm = new AddTitleViewModel(vm.Repository, vm.JikanClient);
                var dialog = new AddTitleWindow { DataContext = addVm };
                var added = await dialog.ShowDialog<bool>(this);
                if (added)
                {
                    await vm.LoadLibraryAsync();
                }
            };

            vm.OpenDetailRequested += async (item) =>
            {
                var detailVm = new MediaDetailViewModel(item, vm.Repository, vm.TrackingService);
                var dialog = new MediaDetailWindow { DataContext = detailVm };
                var changed = await dialog.ShowDialog<bool>(this);
                if (changed)
                {
                    await vm.LoadLibraryAsync();
                }
            };

            _ = vm.LoadLibraryAsync();
        }
    }
}
