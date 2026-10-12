using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AnimeTracker.Tests;

public class MediaDetailViewModelTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _connectionString;
    private readonly MediaItemRepository _repository;
    private readonly MediaTrackingService _trackingService;

    static MediaDetailViewModelTests()
    {
        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
        }
        catch
        {
            // Already initialized
        }
    }

    public MediaDetailViewModelTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"animetracker_detail_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbPath}";
        DatabaseInitializer.Initialize(_connectionString);
        _repository = new MediaItemRepository(_connectionString);
        _trackingService = new MediaTrackingService(_repository);
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
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void InitializesWithItemData_CorrectlyPopulatesProperties()
    {
        var item = new MediaItem
        {
            Id = 1,
            Title = "Frieren",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 12,
            TotalProgress = 28,
            Score = 9,
            RewatchCount = 0,
            Genres = "Adventure, Fantasy",
            Synopsis = "Elf journey",
            MalId = 52991,
            MalType = "anime"
        };

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);

        Assert.Equal("Frieren", vm.Title);
        Assert.Equal(MediaType.Anime, vm.Type);
        Assert.Equal(MediaStatus.Watching, vm.Status);
        Assert.Equal(12, vm.CurrentProgress);
        Assert.Equal(28, vm.TotalProgress);
        Assert.Equal(9, vm.Score);
        Assert.Equal("Adventure, Fantasy", vm.Genres);
        Assert.Equal("Elf journey", vm.Synopsis);
        Assert.Equal(0, vm.RewatchCount);
        Assert.Equal(52991, vm.MalId);
        Assert.Equal("anime", vm.MalType);
        Assert.Equal("Start Rewatch", vm.RewatchActionLabel);
        Assert.False(vm.CanRewatch);
    }

    [Fact]
    public void AvailableStatuses_MatchesMediaType()
    {
        var anime = new MediaItem { Title = "Anime", Type = MediaType.Anime, Status = MediaStatus.Watching };
        var manga = new MediaItem { Title = "Manga", Type = MediaType.Manga, Status = MediaStatus.Reading };

        var animeVm = new MediaDetailViewModel(anime, _repository, _trackingService);
        var mangaVm = new MediaDetailViewModel(manga, _repository, _trackingService);

        Assert.Contains(MediaStatus.Watching, animeVm.AvailableStatuses);
        Assert.Contains(MediaStatus.PlanToWatch, animeVm.AvailableStatuses);
        Assert.DoesNotContain(MediaStatus.Reading, animeVm.AvailableStatuses);
        Assert.DoesNotContain(MediaStatus.PlanToRead, animeVm.AvailableStatuses);

        Assert.Contains(MediaStatus.Reading, mangaVm.AvailableStatuses);
        Assert.Contains(MediaStatus.PlanToRead, mangaVm.AvailableStatuses);
        Assert.DoesNotContain(MediaStatus.Watching, mangaVm.AvailableStatuses);
        Assert.DoesNotContain(MediaStatus.PlanToWatch, mangaVm.AvailableStatuses);
    }

    [Fact]
    public async Task Validation_Fails_WhenTitleIsEmpty()
    {
        var item = new MediaItem { Title = "Valid", Type = MediaType.Anime, Status = MediaStatus.Watching };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.Title = "   ";

        var saved = await vm.SaveAsync();

        Assert.False(saved);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Title", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Equal("Valid", fresh!.Title);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-5)]
    public async Task Validation_Fails_WhenScoreIsOutOfRange(int invalidScore)
    {
        var item = new MediaItem { Title = "Valid", Type = MediaType.Anime, Status = MediaStatus.Watching, Score = 5 };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.Score = invalidScore;

        var saved = await vm.SaveAsync();

        Assert.False(saved);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Score", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validation_Fails_WhenTotalProgressIsNegative()
    {
        var item = new MediaItem { Title = "Valid", Type = MediaType.Anime, Status = MediaStatus.Watching };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.TotalProgress = -1;

        var saved = await vm.SaveAsync();

        Assert.False(saved);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Total progress", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validation_Fails_WhenTotalProgressIsLessThanCurrentProgress()
    {
        var item = new MediaItem { Title = "Valid", Type = MediaType.Anime, Status = MediaStatus.Watching, CurrentProgress = 15 };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.TotalProgress = 10;

        var saved = await vm.SaveAsync();

        Assert.False(saved);
        Assert.NotNull(vm.ErrorMessage);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Null(fresh!.TotalProgress);
    }

    [Fact]
    public async Task Validation_Fails_WhenStatusIsInvalidForMediaType()
    {
        var item = new MediaItem { Title = "Valid", Type = MediaType.Anime, Status = MediaStatus.Watching };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.Status = MediaStatus.Reading; // Invalid for Anime

        var saved = await vm.SaveAsync();

        Assert.False(saved);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_UpdatesItemInRepository_AndSignalsSuccess()
    {
        var item = new MediaItem
        {
            Title = "Old Title",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 5,
            TotalProgress = 12,
            Score = null
        };
        await _repository.AddAsync(item);

        var closeRequested = false;
        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.RequestClose += (s, e) => closeRequested = true;

        vm.Title = "New Title";
        vm.Status = MediaStatus.OnHold;
        vm.Score = 8;
        vm.TotalProgress = 24;
        vm.Genres = "Action, Sci-Fi";
        vm.Synopsis = "A brand new plot synopsis.";

        var saved = await vm.SaveAsync();

        Assert.True(saved);
        Assert.True(vm.WasSavedSuccessfully);
        Assert.True(closeRequested);
        Assert.Null(vm.ErrorMessage);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.NotNull(fresh);
        Assert.Equal("New Title", fresh.Title);
        Assert.Equal(MediaStatus.OnHold, fresh.Status);
        Assert.Equal(8, fresh.Score);
        Assert.Equal(24, fresh.TotalProgress);
        Assert.Equal("Action, Sci-Fi", fresh.Genres);
        Assert.Equal("A brand new plot synopsis.", fresh.Synopsis);
    }

    [Fact]
    public async Task MarkCompletedAsync_CallsTrackingService_AndRefreshesViewModel()
    {
        var item = new MediaItem
        {
            Title = "Solo Leveling",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 10,
            TotalProgress = 12
        };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        await vm.MarkCompletedAsync();

        Assert.Equal(MediaStatus.Completed, vm.Status);
        Assert.Equal(12, vm.CurrentProgress);
        Assert.True(vm.CanRewatch);
        Assert.True(vm.HasUnsavedChangesOrActionOccurred);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Equal(MediaStatus.Completed, fresh!.Status);
        Assert.Equal(12, fresh.CurrentProgress);
    }

    [Fact]
    public async Task StartRewatchAsync_Anime_IncrementsRewatchCount_AndResetsProgress()
    {
        var item = new MediaItem
        {
            Title = "Fullmetal Alchemist",
            Type = MediaType.Anime,
            Status = MediaStatus.Completed,
            CurrentProgress = 64,
            TotalProgress = 64,
            RewatchCount = 0
        };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        Assert.True(vm.CanRewatch);
        Assert.Equal("Start Rewatch", vm.RewatchActionLabel);

        await vm.StartRewatchAsync();

        Assert.Equal(MediaStatus.Watching, vm.Status);
        Assert.Equal(0, vm.CurrentProgress);
        Assert.Equal(1, vm.RewatchCount);
        Assert.False(vm.CanRewatch);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Equal(MediaStatus.Watching, fresh!.Status);
        Assert.Equal(0, fresh.CurrentProgress);
        Assert.Equal(1, fresh.RewatchCount);
    }

    [Fact]
    public async Task StartRewatchAsync_Manga_SetsStatusToReading()
    {
        var item = new MediaItem
        {
            Title = "Berserk",
            Type = MediaType.Manga,
            Status = MediaStatus.Completed,
            CurrentProgress = 370,
            TotalProgress = null,
            RewatchCount = 1
        };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        Assert.True(vm.CanRewatch);
        Assert.Equal("Start Reread", vm.RewatchActionLabel);

        await vm.StartRewatchAsync();

        Assert.Equal(MediaStatus.Reading, vm.Status);
        Assert.Equal(0, vm.CurrentProgress);
        Assert.Equal(2, vm.RewatchCount);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Equal(MediaStatus.Reading, fresh!.Status);
        Assert.Equal(0, fresh.CurrentProgress);
        Assert.Equal(2, fresh.RewatchCount);
    }

    [Fact]
    public async Task DeleteAsync_WhenConfirmed_RemovesFromRepository_AndSignalsClose()
    {
        var item = new MediaItem
        {
            Title = "To Be Deleted",
            Type = MediaType.Anime,
            Status = MediaStatus.Dropped
        };
        await _repository.AddAsync(item);

        var closeRequested = false;
        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.RequestClose += (s, e) => closeRequested = true;

        Assert.False(vm.IsConfirmingDelete);
        vm.RequestDelete();
        Assert.True(vm.IsConfirmingDelete);

        var deleted = await vm.ConfirmDeleteAsync();

        Assert.True(deleted);
        Assert.True(vm.WasDeleted);
        Assert.True(closeRequested);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Null(fresh);
    }

    [Fact]
    public async Task IncrementAndDecrementProgressAsync_CallsTrackingService_AndRefreshesCurrentProgress()
    {
        var item = new MediaItem
        {
            Title = "Steins;Gate",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 5,
            TotalProgress = 24
        };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        await vm.IncrementProgressAsync();

        Assert.Equal(6, vm.CurrentProgress);
        Assert.True(vm.HasUnsavedChangesOrActionOccurred);

        await vm.DecrementProgressAsync();
        Assert.Equal(5, vm.CurrentProgress);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Equal(5, fresh!.CurrentProgress);
    }

    [Fact]
    public async Task Cancel_WithoutSaving_DoesNotMutateItemOrDatabase()
    {
        var item = new MediaItem
        {
            Title = "Original Title",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 3,
            TotalProgress = 12,
            Score = 7
        };
        await _repository.AddAsync(item);

        var closeRequested = false;
        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.RequestClose += (s, e) => closeRequested = true;

        vm.Title = "Edited But Cancelled";
        vm.Status = MediaStatus.Dropped;
        vm.Score = 2;
        vm.TotalProgress = 50;

        vm.Cancel();

        Assert.True(closeRequested);
        Assert.False(vm.WasSavedSuccessfully);
        Assert.False(vm.HasUnsavedChangesOrActionOccurred);
        Assert.Equal("Original Title", item.Title);
        Assert.Equal(MediaStatus.Watching, item.Status);
        Assert.Equal(7, item.Score);

        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Equal("Original Title", fresh!.Title);
        Assert.Equal(MediaStatus.Watching, fresh.Status);
        Assert.Equal(7, fresh.Score);
    }

    [Fact]
    public async Task DirectProgressEditing_WhenValid_SavesToDatabaseOnSave()
    {
        var item = new MediaItem
        {
            Title = "One Piece",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 50,
            TotalProgress = null
        };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.CurrentProgress = 1100;

        var saved = await vm.SaveAsync();

        Assert.True(saved);
        var fresh = await _repository.GetByIdAsync(item.Id);
        Assert.Equal(1100, fresh!.CurrentProgress);
    }

    [Fact]
    public async Task DirectProgressEditing_WhenNegative_RejectsWithErrorMessage()
    {
        var item = new MediaItem
        {
            Title = "Naruto",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 10,
            TotalProgress = 220
        };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.CurrentProgress = -3;

        var saved = await vm.SaveAsync();

        Assert.False(saved);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("negative", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectProgressEditing_WhenExceedingTotal_RejectsWithErrorMessage()
    {
        var item = new MediaItem
        {
            Title = "Attack on Titan",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 10,
            TotalProgress = 25
        };
        await _repository.AddAsync(item);

        var vm = new MediaDetailViewModel(item, _repository, _trackingService);
        vm.CurrentProgress = 30;

        var saved = await vm.SaveAsync();

        Assert.False(saved);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("exceed", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgressUnitLabel_ReturnsEpisodeForAnime_AndChapterForManga()
    {
        var anime = new MediaItem { Title = "Anime", Type = MediaType.Anime, Status = MediaStatus.Watching };
        var manga = new MediaItem { Title = "Manga", Type = MediaType.Manga, Status = MediaStatus.Reading };
        var manhwa = new MediaItem { Title = "Manhwa", Type = MediaType.Manhwa, Status = MediaStatus.Reading };

        var animeVm = new MediaDetailViewModel(anime, _repository, _trackingService);
        var mangaVm = new MediaDetailViewModel(manga, _repository, _trackingService);
        var manhwaVm = new MediaDetailViewModel(manhwa, _repository, _trackingService);

        Assert.Equal("Episode", animeVm.ProgressUnitLabel);
        Assert.Equal("Chapter", mangaVm.ProgressUnitLabel);
        Assert.Equal("Chapter", manhwaVm.ProgressUnitLabel);
    }

    [Fact]
    public async Task SetSelectedCoverFile_ValidFile_SetsPreviewAndSavesOnSave()
    {
        var coversDir = Path.Combine(Path.GetTempPath(), $"animetracker_detailcovers_{Guid.NewGuid():N}");
        Directory.CreateDirectory(coversDir);
        var storage = new CoverStorageService(coversDir);

        var tempImageFile = Path.Combine(Path.GetTempPath(), $"temp_cover_{Guid.NewGuid():N}.png");
        byte[] validPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
        await File.WriteAllBytesAsync(tempImageFile, validPng);

        try
        {
            var item = new MediaItem
            {
                Title = "Chainsaw Man",
                Type = MediaType.Anime,
                Status = MediaStatus.Watching
            };
            await _repository.AddAsync(item);

            var vm = new MediaDetailViewModel(item, _repository, _trackingService, storage);
            vm.SetSelectedCoverFile(tempImageFile);

            Assert.True(vm.HasCustomCover);
            Assert.NotNull(vm.CoverImage);

            var saved = await vm.SaveAsync();
            Assert.True(saved);

            var fresh = await _repository.GetByIdAsync(item.Id);
            Assert.NotNull(fresh!.CoverImagePath);
            Assert.StartsWith("covers/", fresh.CoverImagePath!);
            Assert.True(File.Exists(storage.GetFullPath(fresh.CoverImagePath!)));
        }
        finally
        {
            if (File.Exists(tempImageFile)) File.Delete(tempImageFile);
            if (Directory.Exists(coversDir)) Directory.Delete(coversDir, true);
        }
    }

    [Fact]
    public async Task RemoveCustomCover_RemovesCoverOnSave()
    {
        var coversDir = Path.Combine(Path.GetTempPath(), $"animetracker_detailcovers_{Guid.NewGuid():N}");
        Directory.CreateDirectory(coversDir);
        var storage = new CoverStorageService(coversDir);

        byte[] validPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
        var savedRelativePath = await storage.SaveCoverAsync(validPng);

        try
        {
            var item = new MediaItem
            {
                Title = "Solo Leveling",
                Type = MediaType.Manhwa,
                Status = MediaStatus.Reading,
                CoverImagePath = savedRelativePath
            };
            await _repository.AddAsync(item);

            var vm = new MediaDetailViewModel(item, _repository, _trackingService, storage);
            Assert.True(vm.HasCustomCover);

            vm.RemoveCustomCover();
            Assert.False(vm.HasCustomCover);

            var saved = await vm.SaveAsync();
            Assert.True(saved);

            var fresh = await _repository.GetByIdAsync(item.Id);
            Assert.Null(fresh!.CoverImagePath);
            Assert.False(File.Exists(storage.GetFullPath(savedRelativePath)));
        }
        finally
        {
            if (Directory.Exists(coversDir)) Directory.Delete(coversDir, true);
        }
    }
}
