using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;

namespace AnimeTracker.Services;

public class MediaTrackingService
{
    private readonly MediaItemRepository _repository;

    public MediaTrackingService(MediaItemRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<MediaItem> IncrementProgressAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"MediaItem with ID {id} not found.");

        item.TryIncrementProgress();
        await _repository.UpdateAsync(item);
        return item;
    }

    public async Task<MediaItem> DecrementProgressAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"MediaItem with ID {id} not found.");

        item.TryDecrementProgress();
        await _repository.UpdateAsync(item);
        return item;
    }

    public async Task<MediaItem> CompleteAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"MediaItem with ID {id} not found.");

        item.MarkCompleted();
        await _repository.UpdateAsync(item);
        return item;
    }

    public async Task<MediaItem> StartRewatchAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"MediaItem with ID {id} not found.");

        item.StartRewatch();
        await _repository.UpdateAsync(item);
        return item;
    }
}
