using System;
using AnimeTracker.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

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

            vm.RequestExportRequested += async () =>
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export AnimeTracker Backup",
                    DefaultExtension = "json",
                    SuggestedFileName = $"animetracker_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("JSON Backup (*.json)")
                        {
                            Patterns = new[] { "*.json" },
                            MimeTypes = new[] { "application/json" }
                        }
                    }
                });

                if (file != null)
                {
                    await vm.ExportToFileAsync(file.Path.LocalPath);
                }
            };

            vm.RequestImportRequested += async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select AnimeTracker JSON Backup",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("JSON Backup (*.json)")
                        {
                            Patterns = new[] { "*.json" },
                            MimeTypes = new[] { "application/json" }
                        }
                    }
                });

                if (files != null && files.Count > 0)
                {
                    await vm.PrepareImportFromFileAsync(files[0].Path.LocalPath);
                }
            };

            _ = vm.LoadLibraryAsync();
        }
    }
}
