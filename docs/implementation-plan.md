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

### 8. Fázis: Backlog-ajánlórendszer (KÖVETKEZŐ LÉPÉS)
- [ ] **8.1. Ajánló domain logika és heurisztika (`RecommendationService`):**
  - Bemenet: legalább 8/10-re értékelt könyvtári címek (`Score >= 8`).
  - Kedvenc műfajok gyakoriságának és súlyozott pontszámának számítása.
  - Célcsoport: KIZÁRÓLAG a még el nem kezdett (`PlanToWatch`, `PlanToRead`) elemek.
  - Kizárások: már nézett/olvasott, befejezett, félretett, eldobott címek kizárása.
  - Pontozás: műfaji egyezések aránya és pontszáma alapján rendezett lista.
  - Tényszerű indoklás generálása (pl. megnevezett egyező műfajok alapján).
  - Szélsőértékek kezelése: nincs 8+-os cím, nincs backlog elem, nincsenek műfajok.
  - TDD tesztek: `tests/AnimeTracker.Tests/RecommendationServiceTests.cs`.
- [ ] **8.2. Ajánló ViewModel és szűrés:**
  - `RecommendationsViewModel` vagy `MainWindowViewModel` integráció.
  - Típusszűrő (Mind, Anime, Manga, Manhwa) és műfajszűrő támogatása.
  - Üres állapotok lekezelése és informatív megjelenítése.
  - Gyorsakció: cím megnyitása részletezéshez / állapot módosításához.
  - TDD tesztek.
- [ ] **8.3. Ajánló felület (Avalonia UI):**
  - Ajánló nézet (`RecommendationsView.axaml` vagy beágyazott tartalom a `MainWindow.axaml`-ben a `Recommendations` szekcióhoz).
  - Ajánlási pontszám, indoklás badge és borító megjelenítése.

---

### 9. Fázis: Biztonsági mentés – JSON export és tranzakciós import (TERVEZETT)
- [ ] **9.1. Export/Import DTO és séma:**
  - `LibraryBackupDto` verziószámmal (`schemaVersion: 1`), exportálási időbélyeggel és tétellistával.
  - System.Text.Json formázott szerializáció.
- [ ] **9.2. BackupService üzleti logika:**
  - `ExportAsync`: Olvasás a repository-ból, JSON fájlba írás atomi módon anélkül, hogy az adatbázist módosítaná.
  - `ValidateImport`: JSON formátum, sémaverzió, rekordstruktúra, kötelező mezők (`Title`), érvényes enums, nem negatív számok, belső katalógus duplikációk `(MalId, MalType)` ellenőrzése.
  - `ImportReplaceAsync`: Teljes csere modell. Kizárólag explicit megerősítés után. Egyetlen SQLite tranzakcióban (`DELETE FROM MediaItems; INSERT ...`). Hiba esetén azonnali rollback.
  - TDD tesztek: `tests/AnimeTracker.Tests/BackupServiceTests.cs`.
- [ ] **9.3. Beállítások és mentés ViewModel & UI:**
  - `SettingsAndBackup` nézet gombokkal, fájlválasztóval (`StorageProvider`), megerősítő dialógussal és státuszvisszajelzéssel.
  - Sikeres import után a könyvtár és a számlálók azonnali újratöltése.

---

### 10. Fázis: Integráció és végső minőség-ellenőrzés (TERVEZETT)
- [ ] Teljes xUnit tesztkészlet (`dotnet test`).
- [ ] Release fordítás (`dotnet build -c Release`).
- [ ] Alkalmazásindítási és manuális működési ellenőrzés.
- [ ] Végső riport és dokumentáció frissítés.
