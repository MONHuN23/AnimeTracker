# 🎬 AnimeTracker

[![.NET 10](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia%20UI-12.x-8430b5?logo=avalonia&logoColor=white)](https://avaloniaui.net/)
[![SQLite](https://img.shields.io/badge/Database-SQLite%20%2B%20Dapper-003B57?logo=sqlite&logoColor=white)](https://sqlite.org/)
[![Tests](https://img.shields.io/badge/Tests-148%20Passing-brightgreen)](https://xunit.net/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**AnimeTracker** is a modern, lightweight, privacy-focused cross-platform desktop application designed to track your anime, manga, and manhwa progress. Built with **.NET 10 LTS** and **Avalonia UI 12**, it combines a clean graphite-purple dark theme with local-first SQLite storage, online metadata searching, and an intelligent backlog recommendation system.

---

## ✨ Features

- 🎨 **Modern Dark Graphite Theme**
  - Custom dark theme optimized for readability and long viewing sessions.
  - Responsive card grid displaying posters, status badges, progress steppers, and personal ratings.
  - Multi-tab navigation with dynamic counters for *All*, *Watching / Reading*, *Completed*, *On Hold*, *Dropped*, and *Plan to Watch / Read*.

- 🔍 **Jikan API v4 & Catalog Search**
  - Real-time catalog search for Anime, Manga, and Manhwa using Jikan v4 (MyAnimeList data).
  - Debounced input (400–500 ms) with request cancellation (`CancellationToken`) preventing race conditions.
  - Resilient fallback support and duplicate detection by `(MalId, MalType)`.

- ✍️ **Manual Entry & Custom Titles**
  - Complete offline entry for titles not found in online catalogs or custom works.
  - Direct starting progress input that automatically sets status to *Watching* or *Reading* when progress > 0.
  - Custom total episode / chapter configuration, genres, and synopses.

- 🖼️ **Cover Images (Local Upload & Remote)**
  - Support for uploading custom cover images from your computer (PNG, JPEG, WebP up to 10 MB).
  - Magic-byte validation verifying actual file headers rather than just extensions.
  - Image preview, replacement, and safe deletion without orphaned files.
  - Automatic fallback hierarchy: Local Custom Cover → Remote Catalog Cover → Placeholder star.

- 🔢 **Direct Progress Editing & Tracking**
  - Incremental progress buttons (`+` / `−`) directly on library cards for quick logging.
  - Full detail and edit modal with direct numeric input (e.g. Episode 1200) and media-specific units (*Episodes* for Anime, *Chapters* for Manga/Manhwa).
  - One-click *Mark as Completed* and *Start Rewatch / Reread* operations with automatic counter tracking.

- 🎯 **Smart Backlog Recommendations**
  - Intelligent heuristic engine that analyzes your favorite genres based on titles rated 8/10 or higher.
  - Ranks unstarted backlog titles (*Plan to Watch* / *Plan to Read*) and generates clear explanations for why a title is recommended.
  - Filter recommendations by media type (Anime, Manga, Manhwa) and top preferred genres.

- 💾 **Data Portability & Transactional Backups**
  - Full JSON export of your library.
  - Embeds local cover images in Base64 within the backup JSON envelope for seamless migration between machines.
  - Pre-import validation checking JSON schema versioning, field integrity, and catalog constraints.
  - Atomic SQLite replacement import with transaction rollback and filesystem cleanup if any error occurs.
  - Backward compatible with older backups without Base64 images.

---

## 🛠️ Tech Stack & Architecture

- **Platform:** C# / .NET 10 LTS
- **UI Framework:** [Avalonia UI](https://avaloniaui.net/) 12.x (Desktop XAML, MVVM)
- **MVVM Toolkit:** `CommunityToolkit.Mvvm` (Source generators, ObservableProperty, RelayCommand)
- **Data Access:** Local SQLite via `Microsoft.Data.Sqlite` + [Dapper](https://github.com/DapperLib/Dapper) micro-ORM (Strictly no heavy EF Core)
- **External API:** Jikan API v4 via `System.Net.Http.HttpClient` & `System.Text.Json`
- **Testing:** [xUnit](https://xunit.net/) with 148 automated unit and integration tests (TDD workflow)

### Architectural Design

```text
AnimeTracker/
├── src/AnimeTracker/
│   ├── Data/                  # SQLite connection, Dapper repository, schema migrations
│   ├── Models/                # Domain models (MediaItem, MediaType, MediaStatus)
│   ├── Services/
│   │   ├── Backup/            # JSON export/import DTOs, validation & transaction logic
│   │   ├── Jikan/             # Jikan REST API client & DTO mapping
│   │   ├── CoverStorageService.cs # Image validation, local storage & lifecycle management
│   │   ├── ImageLoader.cs     # Asynchronous Avalonia Bitmap loading & caching
│   │   ├── MediaTrackingService.cs # Progress bounds, completion & rewatch domain logic
│   │   └── RecommendationService.cs # Affinity scoring & backlog recommendation engine
│   ├── ViewModels/            # MVVM ViewModels (MainWindow, Detail, AddTitle, Cards)
│   └── Views/                 # Avalonia XAML Windows, Dialogs, Themes & Styles
└── tests/AnimeTracker.Tests/  # Comprehensive xUnit test suite (148 tests)
```

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) installed on Linux, macOS, or Windows.
- Git.

### Clone and Build

```bash
# Clone the repository
git clone https://github.com/MONHuN23/AnimeTracker.git
cd AnimeTracker

# Restore dependencies and build in Release mode
dotnet build -c Release
```

### Running the Application

```bash
dotnet run --project src/AnimeTracker -c Release
```

### Running Tests

To run the automated test suite:

```bash
dotnet test
```

All 148 tests run sequentially without external dependencies or live network access required.

---

## 📁 Storage Locations

By default, AnimeTracker stores user data in your local application directory:

- **Linux:** `~/.local/share/AnimeTracker/`
  - Database: `library.db`
  - Cover images: `covers/`
- **Windows:** `%LOCALAPPDATA%\AnimeTracker\`
- **macOS:** `~/Library/Application Support/AnimeTracker/`

---

## 📜 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
