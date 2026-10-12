# AnimeTracker – Implementációs Terv

Ez a dokumentum rögzíti az AnimeTracker projekt fejlesztési tervét, a specifikáció követelményeinek állapotát, a feladatok közötti függőségeket és az ellenőrzési lépéseket.

---

## 1. Technológiai alapok és irányelvek
- **Nyelv és futtatókörnyezet:** C# / .NET 10 LTS
- **UI keretrendszer:** Avalonia UI 12.x
- **Architektúra:** MVVM (`CommunityToolkit.Mvvm`)
- **Adattárolás:** Helyi SQLite (`Microsoft.Data.Sqlite`, Dapper micro-ORM, tranzakciókezelés; EF Core kizárva)
- **Külső API:** Jikan API v4 (REST, `HttpClient`, `System.Text.Json`)
- **Tesztelés:** xUnit automatizált tesztek (TDD módszertan)

---

## 2. Feladatok és aktuális állapot

### 1. Fázis: Projektváz és alapinfrastruktúra (KÉSZ)
- [x] Projektstruktúra (`src/AnimeTracker`, `tests/AnimeTracker.Tests`).
- [x] Függőségek és tesztprojekt beállítása.
- [x] Ellenőrzés: sikeres build, tesztfuttatás.

### 2. Fázis: Adatmodell és SQLite perzisztencia (KÉSZ)
- [x] `MediaItem`, `MediaType`, `MediaStatus` típusok.
- [x] SQLite séma inicializálás (`DatabaseInitializer`) duplikációvédelmi indexszel `(MalId, MalType)`.
- [x] `MediaItemRepository` (CRUD műveletek Dapperrel).
- [x] Ellenőrzés: `MediaItemRepositoryTests` (10 teszt).

### 3. Fázis: Üzleti logika és haladáskövetés (KÉSZ)
- [x] `TryIncrementProgress`, `TryDecrementProgress`, `MarkCompleted`, `StartRewatch`.
- [x] `MediaTrackingService` perzisztens műveletek.
- [x] Ellenőrzés: `MediaTrackingServiceTests` (16 teszt).

### 4. Fázis: Jikan API-kliens (KÉSZ)
- [x] `JikanApiClient` anime és manga/manhwa kereséssel.
- [x] DTO leképezés (`JikanSearchResult`).
- [x] Ellenőrzés: `JikanApiClientTests` (11 teszt).

### 5. Fázis: Új cím felvétele (Add Title) (KÉSZ)
- [x] `AddTitleViewModel` Jikan kereséssel (debounce, duplikációvédelem) és kézi felvitellel.
- [x] `AddTitleWindow.axaml` dialógus felület.
- [x] Ellenőrzés: `AddTitleViewModelTests` (11 teszt).

### 6. Fázis: Könyvtárnézet, kártyarács és szűrők (KÉSZ)
- [x] `MediaCardViewModel` inline stepperekkel és borítóbetöltéssel.
- [x] `ImageLoader` aszinkron távoli képletöltő memóriacache-sel.
- [x] `MainWindowViewModel` szekció- és típusszűréssel, rendezéssel, kereséssel, számlálókkal.
- [x] `MainWindow.axaml` sötét téma, navigációs oldalsáv, responsive rács.
- [x] Ellenőrzés: `MainWindowLibraryViewModelTests` (11 teszt).

### 7. Fázis: Részletező és szerkesztő ablak (KÉSZ, ellenőrizve)
- [x] `MediaDetailViewModel` típusfüggő státuszokkal, validációval, mentéssel, befejezéssel, újranézéssel, törlés megerősítéssel és nem mentett változtatások eldobásával (`Cancel`).
- [x] `MediaDetailWindow.axaml` kétoszlopos modális nézet sötét témával.
- [x] Kártyára kattintáskor a friss adatbázis-rekord betöltése és a modális ablak megnyitása.
- [x] Mentés, állapotváltás vagy törlés utáni könyvtár- és számlálófrissítés.
- [x] Ellenőrzés: `MediaDetailViewModelTests` (18 teszt).

---

### 8. Fázis: Backlog-ajánlórendszer (KÉSZ)
- [x] **8.1. Ajánló domain logika és heurisztika (`RecommendationService`):**
  - Bemenet: legalább 8/10-re értékelt könyvtári címek (`Score >= 8`).
  - Kedvenc műfajok gyakoriságának és súlyozott pontszámának számítása.
  - Célcsoport: KIZÁRÓLAG a még el nem kezdett (`PlanToWatch`, `PlanToRead`) elemek.
  - Kizárások: már nézett/olvasott, befejezett, félretett, eldobott címek kizárása.
  - Pontozás: műfaji egyezések aránya és pontszáma alapján rendezett lista.
  - Tényszerű indoklás generálása (pl. megnevezett egyező műfajok alapján).
  - Szélsőértékek kezelése: nincs 8+-os cím, nincs backlog elem, nincsenek műfajok.
  - Ellenőrzés: `RecommendationServiceTests` (10 teszt).
- [x] **8.2. Ajánló ViewModel és szűrés:**
  - `RecommendationCardViewModel` és `MainWindowViewModel` integráció.
  - Típusszűrő (Mind, Anime, Manga, Manhwa) és műfajszűrő támogatása (`TopFavoriteGenres`, `SelectedRecommendationGenre`).
  - Üres állapotok lekezelése és informatív megjelenítése (`RecommendationEmptyMessage`).
  - Gyorsakció: cím megnyitása részletezéshez / állapot módosításához (`OpenRecommendationDetailCommand`).
  - Ellenőrzés: `MainWindowLibraryViewModelTests.RecommendationsSection_ComputesAndDisplaysRankedBacklog`.
- [x] **8.3. Ajánló felület (Avalonia UI):**
  - `MainWindow.axaml` ajánló szekció reszponzív kártyarácsal (`WrapPanel`), poszterrel, csillag fallbackkel, típus- és pontszám-jelvénnyel, indoklás-dobozzal.

---

### 9. Fázis: Biztonsági mentés – JSON export és tranzakciós import (KÉSZ)
- [x] **9.1. Export/Import DTO és séma:**
  - `BackupEnvelope` verziószámmal (`version: 1`), exportálási időbélyeggel (`exportedAt`), tételszámmal és `BackupMediaItemDto` listával.
  - `System.Text.Json` formázott és behúzott szerializáció.
- [x] **9.2. BackupService üzleti logika és TDD:**
  - `ExportToFileAsync`: Olvasás a repository-ból, JSON fájlba írás atomi módon anélkül, hogy az adatbázist módosítaná.
  - `ValidateFileAsync` és `ValidateJson`: JSON formátum, sémaverzió (`version == 1`), kötelező mezők (`Title`), érvényes típusok, státusz- és médiatípus összeférhetőség, `CurrentProgress <= TotalProgress`, pontszám határok (1..10), nem negatív számok, belső katalógus duplikációk `(MalId, MalType)` ellenőrzése adatbázis-művelet nélkül.
  - `ImportReplaceAsync`: Teljes csere modell. Egyetlen SQLite tranzakcióban (`DELETE FROM MediaItems; INSERT ...`). Bármilyen SQL vagy megszorítási hiba esetén azonnali rollback.
  - TDD tesztek: `tests/AnimeTracker.Tests/BackupServiceTests.cs` (10 teszt).
- [x] **9.3. Beállítások és mentés ViewModel & UI:**
  - `MainWindowViewModel`: `ExportToFileAsync`, `PrepareImportFromFileAsync`, `CancelImport`, `ConfirmImportAsync`, `IsImportConfirming`, `ValidatedImportCount`, állapot- és hibajelzések.
  - `MainWindow.axaml.cs`: `StorageProvider` integráció (`SaveFilePickerAsync`, `OpenFilePickerAsync`).
  - `MainWindow.axaml`: Kártyás elrendezés (Exportálás, Importálás & Visszaállítás, Figyelmeztető megerősítő doboz, Névjegy kártya), siker és hiba sávok.
  - Ellenőrzés: `MainWindowLibraryViewModelTests` (6 dedikált mentési teszt, összesen 17 teszt).

---

### 10. Fázis: Integráció és végső minőség-ellenőrzés (KÉSZ)
- [x] Teljes xUnit tesztkészlet (`dotnet test`): 106/106 teszt sikeres.
- [x] Release fordítás (`dotnet build -c Release`): 0 figyelmeztetés, 0 hiba.
- [x] Release tesztfuttatás (`dotnet test -c Release --no-build`): 106/106 teszt sikeres.
- [x] Alkalmazásindítási smoke teszt (`dotnet run -c Release`): hiba nélkül indul, stabilan fut.
- [x] Végső riport és dokumentáció frissítés: minden MVP követelmény maradéktalanul megvalósítva.

---

### 11. Fázis: Saját borítókép feltöltése és kézi haladásbeállítás (KÉSZ)
- [x] **11.1. Helyi borítókép tárolás (`CoverStorageService`):**
  - Képek validálása magic bytes és méretkorlát (10 MB) alapján (JPEG, PNG, WebP).
  - Mentés az alkalmazás adatkönyvtárába (`covers/cover_{guid}.ext`) relatív elérési úttal.
  - Törlés és bájt-lekérés támogatása.
  - Ellenőrzés: `CoverStorageServiceTests` (10 teszt).
- [x] **11.2. SQLite migráció és perzisztencia:**
  - `CoverImagePath TEXT` oszlop hozzáadása `MediaItems` táblához és automatikus `PRAGMA table_info` migráció.
  - `MediaItemRepository` CRUD műveletek frissítése.
  - Ellenőrzés: `MediaItemRepositoryTests` (10 teszt).
- [x] **11.3. Képbetöltő bővítése (`ImageLoader`):**
  - Helyi képfájlok és memóriabájtok betöltése.
  - Elsőbbségi sorrend: helyi borító > távoli URL > placeholder csillag.
  - Sérült/hiányzó képek esetén hibabiztos fallback összeomlás nélkül.
  - Ellenőrzés: `ImageLoaderTests` (6 teszt).
- [x] **11.4. Új cím kézi felvitele (Manual Entry):**
  - Kezdő haladás (Starting Progress) beállítása; ha > 0, automatikus státusz: `Watching` / `Reading`.
  - Határérték-ellenőrzés: nem lehet negatív és nem haladhatja meg az ismert összegész haladást.
  - Borítókép böngészése Avalonia `StorageProvider`-rel, előnézet, törlés, mentés `CoverStorageService`-szel.
  - Hiba esetén árva fájlok automatikus törlése.
  - Ellenőrzés: `AddTitleViewModelTests` (17 teszt).
- [x] **11.5. Részletező ablak (MediaDetail):**
  - Közvetlenül szerkeszthető haladás numerikus beviteli mezővel (`NumericUpDown`).
  - Dinamikus egységfelirat: Anime esetén "Current Episode *", Manga/Manhwa esetén "Current Chapter *".
  - Meglévő `+` és `−` stepperek megtartása.
  - Saját borítókép választása és eltávolítása előnézettel; mentéskor frissítés, törléskor előző borító eltávolítása.
  - `Cancel` esetén sem a haladás, sem a képek nem módosulnak az adatbázisban.
  - Ellenőrzés: `MediaDetailViewModelTests` (22 teszt).
- [x] **11.6. Kártyanézetek integrációja:**
  - `MediaCardViewModel` és `RecommendationCardViewModel` aszinkron helyi és távoli borítóbetöltése (`LoadCoverAsync`).
  - Ellenőrzés: `MediaCardViewModelTests` (2 teszt).
- [x] **11.7. JSON biztonsági mentés kiterjesztése:**
  - `BackupEnvelope` és `BackupMediaItemDto` Base64 képadatokkal (`CoverImageBase64`, `CoverImagePath`).
  - Exportálás kódolja a helyi borítókat Base64 formátumban.
  - Importálás validálja a képadatokat és atomi módon kicsomagolja `covers/` mappába.
  - Tranzakciós rollback: adatbázis hiba esetén a frissen kiírt képfájlok automatikus törlése.
  - Visszafelé kompatibilitás régebbi, képadat nélküli mentésekkel.
  - Ellenőrzés: `BackupServiceTests` (15 teszt).
- [x] **11.8. Minőségi kapu:**
  - `dotnet test`: 148/148 teszt sikeres.
  - `dotnet build -c Release`: 0 figyelmeztetés, 0 hiba.
  - `dotnet test -c Release --no-build`: 148/148 teszt sikeres.

