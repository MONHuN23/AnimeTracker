using System;
using System.IO;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AnimeTracker.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _connectionString;
    private readonly MediaItemRepository _repository;

    public MainWindowViewModelTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"animetracker_mainvm_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbPath}";
        DatabaseInitializer.Initialize(_connectionString);
        _repository = new MediaItemRepository(_connectionString);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try
            {
                SqliteConnection.ClearAllPools();
                File.Delete(_tempDbPath);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Fact]
    public void Constructor_InitializesWithCurrentlyActiveSection()
    {
        var vm = new MainWindowViewModel(_repository);

        Assert.Equal(NavigationSection.CurrentlyActive, vm.SelectedSection);
        Assert.Equal("Currently Active (Watching / Reading)", vm.CurrentSectionTitle);
        Assert.Equal(0, vm.ActiveCount);
        Assert.Equal(0, vm.TotalLibraryCount);
    }

    [Theory]
    [InlineData(NavigationSection.AllLibrary, "All Library")]
    [InlineData(NavigationSection.PlanToWatchRead, "Plan to Watch / Read")]
    [InlineData(NavigationSection.Completed, "Completed")]
    [InlineData(NavigationSection.OnHoldDropped, "On Hold / Dropped")]
    [InlineData(NavigationSection.Recommendations, "Personalized Recommendations")]
    [InlineData(NavigationSection.SettingsAndBackup, "Settings & Backup")]
    public void SelectSectionCommand_UpdatesSelectedSectionAndTitle(NavigationSection section, string expectedTitle)
    {
        var vm = new MainWindowViewModel(_repository);

        vm.SelectSectionCommand.Execute(section);

        Assert.Equal(section, vm.SelectedSection);
        Assert.Equal(expectedTitle, vm.CurrentSectionTitle);
    }

    [Fact]
    public async Task OpenAddTitleCommand_TriggersOpenAddTitleRequestedEvent()
    {
        var vm = new MainWindowViewModel(_repository);
        var eventTriggered = false;
        vm.OpenAddTitleRequested += () =>
        {
            eventTriggered = true;
            return Task.CompletedTask;
        };

        await vm.OpenAddTitleCommand.ExecuteAsync(null);

        Assert.True(eventTriggered);
    }

    [Fact]
    public async Task RefreshCountsAsync_CalculatesCorrectCountsForEachCategory()
    {
        await _repository.AddAsync(new MediaItem { Title = "Anime 1", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Manga 1", Type = MediaType.Manga, Status = MediaStatus.Reading });
        await _repository.AddAsync(new MediaItem { Title = "Anime 2", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch });
        await _repository.AddAsync(new MediaItem { Title = "Anime 3", Type = MediaType.Anime, Status = MediaStatus.Completed });
        await _repository.AddAsync(new MediaItem { Title = "Manga 2", Type = MediaType.Manga, Status = MediaStatus.OnHold });

        var vm = new MainWindowViewModel(_repository);
        await vm.RefreshCountsAsync();

        Assert.Equal(5, vm.TotalLibraryCount);
        Assert.Equal(2, vm.ActiveCount); // Watching (1) + Reading (1)
        Assert.Equal(1, vm.PlanToCount);
        Assert.Equal(1, vm.CompletedCount);
        Assert.Equal(1, vm.OnHoldDroppedCount);
    }
}
