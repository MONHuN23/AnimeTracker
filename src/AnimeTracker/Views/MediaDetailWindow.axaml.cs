using System;
using AnimeTracker.ViewModels;
using Avalonia.Controls;

namespace AnimeTracker.Views;

public partial class MediaDetailWindow : Window
{
    public MediaDetailWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MediaDetailViewModel vm)
        {
            vm.LoadCoverImage();
            vm.RequestClose += (s, args) => Close(vm.HasUnsavedChangesOrActionOccurred);
        }
    }
}
