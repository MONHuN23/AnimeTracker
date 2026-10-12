using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AnimeTracker.Data;

public static class DatabaseInitializer
{
    public static string GetDefaultDataDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(basePath, "AnimeTracker");
    }

    public static string GetDefaultDatabasePath()
    {
        return Path.Combine(GetDefaultDataDirectory(), "library.db");
    }

    public static string GetDefaultConnectionString()
    {
        return $"Data Source={GetDefaultDatabasePath()}";
    }

    public static void Initialize(string? connectionString = null)
    {
        connectionString ??= GetDefaultConnectionString();

        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource) &&
            builder.DataSource != ":memory:" &&
            !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(builder.DataSource);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS MediaItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MalId INTEGER,
                MalType TEXT,
                Title TEXT NOT NULL,
                Type TEXT NOT NULL,
                Status TEXT NOT NULL,
                CurrentProgress INTEGER NOT NULL DEFAULT 0,
                TotalProgress INTEGER,
                Score INTEGER,
                RewatchCount INTEGER NOT NULL DEFAULT 0,
                CoverImageUrl TEXT,
                CoverImagePath TEXT,
                Genres TEXT,
                Synopsis TEXT,
                ReleaseYear INTEGER,
                UpdatedAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_MediaItems_MalId_MalType
                ON MediaItems (MalId, MalType)
                WHERE MalId IS NOT NULL AND MalType IS NOT NULL;
            """;
        cmd.ExecuteNonQuery();

        // Migration check: Ensure CoverImagePath column exists in pre-existing tables
        EnsureCoverImagePathColumnExists(connection);
    }

    private static void EnsureCoverImagePathColumnExists(SqliteConnection connection)
    {
        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA table_info(MediaItems);";
        using var reader = pragmaCmd.ExecuteReader();

        var hasColumn = false;
        while (reader.Read())
        {
            // Column 1 is the column name in PRAGMA table_info
            var colName = reader.GetString(1);
            if (string.Equals(colName, "CoverImagePath", StringComparison.OrdinalIgnoreCase))
            {
                hasColumn = true;
                break;
            }
        }
        reader.Close();

        if (!hasColumn)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE MediaItems ADD COLUMN CoverImagePath TEXT;";
            alterCmd.ExecuteNonQuery();
        }
    }
}
