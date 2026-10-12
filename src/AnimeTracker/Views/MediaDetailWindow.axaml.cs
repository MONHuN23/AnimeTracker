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
            vm.RequestSelectImageFile = async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select Cover Image",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new Avalonia.Platform.Storage.FilePickerFileType("Image Files (*.png, *.jpg, *.jpeg, *.webp)")
                        {
                            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
                            MimeTypes = ["image/png", "image/jpeg", "image/webp"]
                        }
                    ]
                });

                if (files != null && files.Count > 0)
                {
                    return files[0].Path.LocalPath;
                }

                return null;
            };
        }
    }
}
