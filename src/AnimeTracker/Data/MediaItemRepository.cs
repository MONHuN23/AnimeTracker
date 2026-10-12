using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AnimeTracker.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AnimeTracker.Data;

public class StringEnumTypeHandler<T> : SqlMapper.TypeHandler<T> where T : struct, Enum
{
    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.Value = value.ToString();
    }

    public override T Parse(object value)
    {
        if (value is string str && Enum.TryParse<T>(str, true, out var result))
        {
            return result;
        }

        if (value is long or int)
        {
            return (T)Enum.ToObject(typeof(T), value);
        }

        return default;
    }
}

public class MediaItemRepository
{
    private readonly string _connectionString;

    static MediaItemRepository()
    {
        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<MediaType>());
        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<MediaStatus>());
    }

    public MediaItemRepository(string? connectionString = null)
    {
        _connectionString = connectionString ?? DatabaseInitializer.GetDefaultConnectionString();
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    public async Task<int> AddAsync(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var now = DateTime.UtcNow;
        if (item.CreatedAt == default)
        {
            item.CreatedAt = now;
        }

        if (item.UpdatedAt == default)
        {
            item.UpdatedAt = now;
        }

        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = """
            INSERT INTO MediaItems (
                MalId, MalType, Title, Type, Status, CurrentProgress, TotalProgress,
                Score, RewatchCount, CoverImageUrl, Genres, Synopsis, ReleaseYear,
                UpdatedAt, CreatedAt
            ) VALUES (
                @MalId, @MalType, @Title, @Type, @Status, @CurrentProgress, @TotalProgress,
                @Score, @RewatchCount, @CoverImageUrl, @Genres, @Synopsis, @ReleaseYear,
                @UpdatedAt, @CreatedAt
            );
            SELECT last_insert_rowid();
            """;

        var id = await connection.ExecuteScalarAsync<long>(sql, item);
        item.Id = (int)id;
        return (int)id;
    }

    public async Task<MediaItem?> GetByIdAsync(int id)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = """
            SELECT Id, MalId, MalType, Title, Type, Status, CurrentProgress, TotalProgress,
                   Score, RewatchCount, CoverImageUrl, Genres, Synopsis, ReleaseYear,
                   UpdatedAt, CreatedAt
            FROM MediaItems
            WHERE Id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<MediaItem>(sql, new { Id = id });
    }

    public async Task<MediaItem?> GetByMalIdAsync(int malId, string malType)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = """
            SELECT Id, MalId, MalType, Title, Type, Status, CurrentProgress, TotalProgress,
                   Score, RewatchCount, CoverImageUrl, Genres, Synopsis, ReleaseYear,
                   UpdatedAt, CreatedAt
            FROM MediaItems
            WHERE MalId = @MalId AND MalType = @MalType;
            """;

        return await connection.QuerySingleOrDefaultAsync<MediaItem>(sql, new { MalId = malId, MalType = malType });
    }

    public async Task<IReadOnlyList<MediaItem>> GetAllAsync()
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = """
            SELECT Id, MalId, MalType, Title, Type, Status, CurrentProgress, TotalProgress,
                   Score, RewatchCount, CoverImageUrl, Genres, Synopsis, ReleaseYear,
                   UpdatedAt, CreatedAt
            FROM MediaItems
            ORDER BY UpdatedAt DESC;
            """;

        var items = await connection.QueryAsync<MediaItem>(sql);
        return items.ToList();
    }

    public async Task<bool> UpdateAsync(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.UpdatedAt = DateTime.UtcNow;

        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = """
            UPDATE MediaItems SET
                MalId = @MalId,
                MalType = @MalType,
                Title = @Title,
                Type = @Type,
                Status = @Status,
                CurrentProgress = @CurrentProgress,
                TotalProgress = @TotalProgress,
                Score = @Score,
                RewatchCount = @RewatchCount,
                CoverImageUrl = @CoverImageUrl,
                Genres = @Genres,
                Synopsis = @Synopsis,
                ReleaseYear = @ReleaseYear,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """;

        var affected = await connection.ExecuteAsync(sql, item);
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = "DELETE FROM MediaItems WHERE Id = @Id;";
        var affected = await connection.ExecuteAsync(sql, new { Id = id });
        return affected > 0;
    }
}
