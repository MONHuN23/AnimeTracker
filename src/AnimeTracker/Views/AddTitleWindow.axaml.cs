using System;
using AnimeTracker.ViewModels;
using Avalonia.Controls;

namespace AnimeTracker.Views;

public partial class AddTitleWindow : Window
{
    public AddTitleWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is AddTitleViewModel vm)
        {
            vm.RequestClose += (s, args) => Close(vm.WasAddedSuccessfully);
        }
    }
}
