# AnimeTracker – Projektkontextus és Architektúra Útmutató (PROJECT_CONTEXT)

Ez a dokumentum a tartós projektkontextus rögzítésére szolgál az **AnimeTracker** alkalmazáshoz. Célja, hogy új beszélgetésekben, munkamenetekben vagy fejlesztési szakaszokban bármely AI-ügynök vagy fejlesztő azonnal, a teljes kódbázis elölről kezdődő feltérképezése nélkül megértse a rendszer célját, architektúráját, üzleti szabályait és fejlesztési alapelveit.

---

## 1. Projektáttekintés

### 1.1. Cél és alapfunkciók
Az **AnimeTracker** egy modern, pehelykönnyű, adatvédelmi szempontból zárt (local-first) asztali alkalmazás anime epizódok, manga és manhwa fejezetek haladásának követésére.

Főbb funkciók:
- **Könyvtárkezelés:** Címek nyilvántartása médiumtípus (*Anime, Manga, Manhwa*) és státusz (*Watching/Reading, Completed, On Hold, Dropped, Plan to Watch/Read*) szerint.
- **Modern felület:** Avalonia UI 12 alapú, sötét grafit-lila témájú, reszponzív kártyarács poszterekkel, jelvényekkel és azonnali stepperekkel.
- **Katalóguskeresés:** Valós idejű keresés a Jikan API v4-en (MyAnimeList publikus REST API) keresztül 400 ms-os debounce-szal, `CancellationToken` lemondással és duplikációvédelemmel.
- **Kézi felvitel (Manual Entry):** Offline vagy nem listázott címek hozzáadása kezdő haladással (amely automatikusan beállítja az aktív státuszt), testreszabható egységekkel és leírásokkal.
- **Saját borítókép feltöltése:** Helyi képfájlok kiválasztása (PNG, JPEG, WebP <= 10 MB), magic bytes ellenőrzés, előnézet, helyi perzisztens tárolás és hordozható relatív útvonalak kezelése.
- **Közvetlen haladásmódosítás:** Tetszőleges számérték megadása a részletező ablakban (pl. 1200. epizód) típusfüggő egységcímkékkel (*Episode* / *Chapter*).
- **Személyre szabott Jikan és Backlog ajánlórendszer:** A felhasználó legalább 8/10-re értékelt címeiből előállított ízlésprofil alapján a Jikan API v4 (és Kitsu fallback) segítségével új, még fel nem vett címek felkutatása, determinisztikus rangsorolás, duplikációvédelem és közvetlen könyvtárhoz adás (`+ Add to Library`).
- **Biztonsági mentés (JSON Export/Import):** Teljes könyvtár mentése Base64-be ágyazott helyi képekkel, előzetes validációval és atomi SQLite tranzakciós cseremodellel.

### 1.2. Technológiai alapok
- **Futtatókörnyezet:** C# / .NET 10 LTS (`TargetFramework: net10.0`)
- **UI Keretrendszer:** Avalonia UI 12.1.4 (Desktop XAML, FluentTheme sötét variánssal)
- **MVVM minta:** `CommunityToolkit.Mvvm` 8.4.2 (Source generált `[ObservableProperty]`, `[RelayCommand]`)
- **Adattárolás:** Helyi SQLite adatbázis (`Microsoft.Data.Sqlite` 10.0.12, Dapper 2.1.89 micro-ORM; **EF Core szigorúan kizárva**)
- **Külső API:** Jikan API v4 REST kliens (`System.Net.Http.HttpClient` és `System.Text.Json`), Kitsu fallback
- **Tesztelés:** xUnit 2.9.3 automatizált tesztcsomag (202 lefutó teszt, szigorú TDD)

### 1.3. Fő könyvtárak és felelősségek
```text
AnimeTracker/
├── docs/                           # Dokumentáció (specifikáció, tervek, projektkontextus)
├── src/AnimeTracker/               # Alkalmazás forráskódja
│   ├── Data/                       # Adatbázis inicializálás, migrációk és Dapper repository
│   ├── Models/                     # Domain entitások és enumok (MediaItem, MediaType, MediaStatus)
│   ├── Services/                   # Üzleti logika, API kliens, tárolás, ajánló, mentés
│   │   ├── Backup/                 # JSON export/import DTO-k, validáció és import tranzakció
│   │   └── Jikan/                  # Jikan v4 REST API hívások, hibakezelés és DTO leképezés
│   ├── ViewModels/                 # MVVM ViewModel réteg (állapotok, parancsok, szűrések)
│   └── Views/                      # Avalonia XAML nézetek, ablakok és stílusok
└── tests/AnimeTracker.Tests/       # Automatizált xUnit tesztek (205 egység- és integrációs teszt)
```

### 1.4. Parancsok (Futtatás, Tesztelés, Build)
- **Alkalmazás futtatása:**
  ```bash
  dotnet run --project src/AnimeTracker -c Release
  ```
- **Tesztek futtatása:**
  ```bash
  dotnet test
  ```
- **Release build fordítása (minőségi kapu):**
  ```bash
  dotnet build -c Release
  ```

---

## 2. Architektúra és adatfolyamok

### 2.1. MVVM és kompozíciós megközelítés
- A projekt következetesen elválasztja a felületet (XAML + code-behind), az állapotot (ViewModels) és az üzleti/adatlogikát (Services, Repositories, Models).
- **Nincs túlbonyolított DI-konténer:** A ViewModel-ek tiszta konstruktor-paramétereken keresztül kapják meg a függőségeiket (`MainWindowViewModel` kompozícióként hozza létre az alapértelmezett repository-t és szolgáltatásokat).
- **Code-behind felelőssége kizárólag a platform- és dialógusintegráció:** A Window kódok (`MainWindow.axaml.cs`, `AddTitleWindow.axaml.cs`, `MediaDetailWindow.axaml.cs`) csak a modális ablakok megnyitását (`dialog.ShowDialog<bool>(this)`) és az Avalonia `StorageProvider` fájlválasztóját kezelik. Az üzleti logika kizárólag a ViewModel-ekben és Services-ekben fut.

### 2.2. Adatfolyam: Cím hozzáadása (Add Title)
1. **Katalóguskeresés:**
   - A felhasználó beír egy kifejezést az `AddTitleWindow` keresőmezőjébe.
   - `OnSearchQueryChanged`: 400 ms debounce késleltetés indul; az előző futó keresést egy `CancellationTokenSource` lemondja.
   - `JikanApiClient.SearchAnimeAsync` vagy `SearchMangaAsync` lefut.
   - A visszaadott `JikanSearchResult` elemek megjelennek a listában.
   - Mentéskor lefut a `(MalId, MalType)` duplikációellenőrzés a `MediaItemRepository.GetByMalIdAsync` segítségével.
2. **Kézi felvitel (Manual Entry):**
   - Megadható a cím, a típus, az opcionális összhaladás és a kezdő haladás (`ManualCurrentProgress`).
   - Ha a kezdő haladás `> 0`, a státusz automatikusan `Watching` (Anime) vagy `Reading` (Manga/Manhwa) lesz; ha `0`, akkor `PlanToWatch` / `PlanToRead`.
   - Egyéni borítókép tallózható: a `StorageProvider` átadja a fájl elérési útját, a `CoverStorageService` ellenőrzi a méretet (max 10 MB) és a magic byte-okat, majd az `ImageLoader.LoadFromFile` előnézetet készít.
   - Mentéskor a `CoverStorageService.SaveCoverFromFileAsync` elmenti a képet `covers/cover_{guid}.ext` néven, és relatív útvonalat rögzít az adatbázisban. Ha az adatbázis mentése elbukna, a frissen létrehozott képfájl automatikusan törlődik.
3. **Zárás és frissítés:**
   - Sikeres mentéskor a `RequestClose` esemény bezárja a dialógust `true` eredménnyel.
   - A `MainWindow.axaml.cs` ezt észlelve azonnal meghívja a `MainWindowViewModel.LoadLibraryAsync()` metódust.

### 2.3. Adatfolyam: Könyvtárnézet és szűrés (Library)
1. `LoadLibraryAsync`: A `MediaItemRepository.GetAllAsync()` beolvassa az összes elemet az SQLite-ból.
2. A fejléc és az oldalsáv számlálói (`ActiveCount`, `TotalLibraryCount`, `PlanToCount`, `CompletedCount`, `OnHoldDroppedCount`) automatikusan újraszámolódnak.
3. Az `ApplyFilter()` metódus a memóriában szűri az elemeket:
   - Szekció szerint (`CurrentlyActive`, `AllLibrary`, `PlanToWatchRead`, `Completed`, `OnHoldDropped`).
   - Típus szerint (`All`, `Anime`, `Manga`, `Manhwa`).
   - Szabadszavas keresés szerint (címegyezés kis-/nagybetű függetlenül).
   - Rendezés szerint (`LastUpdated`, `Title`, `Score`, `Progress`).
4. Minden megjelenített elemhez egy `MediaCardViewModel` példány jön létre, amely kezeli a gyors `+`/`−` steppereket és aszinkron betölti a borítóképet (`LoadCoverImageAsync`).

### 2.4. Adatfolyam: Haladásmódosítás, befejezés és újranézés
- **Inkrementálás / Dekrementálás:**
  - A kártyán vagy a részletező ablakban lévő `+` és `−` gombok a `MediaTrackingService`-t hívják.
  - Szabály: A haladás nem csökkenhet 0 alá, és nem haladhatja meg a teljes progresszt (ha ismert).
  - Az adatbázis frissül, és a `UpdatedAt` időbélyegző az aktuális UTC időre vált.
- **Közvetlen haladásbeállítás:**
  - A `MediaDetailWindow`-ban a felhasználó közvetlenül beírhatja a haladást a numerikus beviteli mezőbe (pl. 1200).
  - Mentéskor (`SaveAsync`) érvényesítésre kerül, hogy `0 <= CurrentProgress <= TotalProgress`.
- **Befejezés (Mark Completed):**
  - A státusz `Completed`-re változik.
  - Ha a teljes mennyiség ismert, a haladás automatikusan igazodik a teljes darabszámhoz (`CurrentProgress = TotalProgress`).
- **Újranézés / Újraolvasás (Start Rewatch / Reread):**
  - Csak `Completed` státuszú címnél érhető el.
  - `RewatchCount` növekszik 1-gyel, `CurrentProgress` visszaáll 0-ra, a státusz pedig visszavált `Watching`-ra vagy `Reading`-re.

### 2.5. Adatfolyam: Ajánlórendszer (Personalized Jikan & Backlog Engine)
1. **Ízlésprofil felépítése (`BuildTasteProfile`):**
   - Bemenet: Az adatbázisban lévő összes elem és az aktuális médiumszűrő (`MediaTypeFilter`).
   - A rendszer kizárólag a legalább 8/10-re értékelt címeket (`Score >= 8`) veszi figyelembe a szűrésnek megfelelő típusokból (Anime vs. Manga/Manhwa).
   - Kiszámítja a műfajok súlyát: minden kedvenc elem pontszáma hozzáadódik az adott műfaj összsúlyához (`genreWeights`).
   - Ha nincs legalább egy 8/10-es értékelés, a profil üres marad, és egyértelmű útmutatás jelenik meg ("Rate some titles 8/10 or higher...").
2. **Katalóguslekérdezés (`GetExternalRecommendationsAsync`):**
   - A Jikan API v4 `anime?genres={id}&order_by=score&sort=desc` és `manga?genres={id}&order_by=score&sort=desc` végpontjain (illetve Kitsu kategória fallbacken) keresztül lekéri a legnépszerűbb címeket a felhasználó top 1-2 kedvenc műfajában (vagy a kiválasztott műfajban).
   - Memóriabeli gyorsítótár (`_genreCache`, 10 perces TTL) és `CancellationToken` védi a klienst a felesleges hálózati kérésektől és az API rate limit túllépésétől (HTTP 429).
3. **Duplikációvédelem és szűrés:**
   - **Könyvtári kizárás:** A rendszer kiszűri mindazokat a címeket, amelyek stabil azonosító (`MalId, MalType`) vagy normalizált cím alapján már szerepelnek a felhasználó könyvtárában, függetlenül azok státuszától.
   - **Kandidátusok közötti deduplikáció:** Az azonos külső címek vagy azonos azonosítójú találatok egymás között is kiszűrésre kerülnek.
4. **Determinisztikus rangsorolási algoritmus (`RankCandidates`):**
   - **Műfaji affinitás ($S_{genre}$):** A találat műfajainak súlyösszege a felhasználó ízlésprofiljából.
   - **Közösségi minőségi faktor ($S_{quality}$):** A külső pontszám mérsékelt bónusza (`Score * 1.5`).
   - **Diverzitási csillapítás ($D$):** Kategória-dominancia elleni szorzó az azonos elsődleges műfajú címeknél.
   - Végső determinisztikus pontszám: `Math.Round((S_genre * 2.0 * D) + S_quality, 1)`.
   - Tényszerű indoklás generálása (pl. *"Matches your highly rated genres: Action, Fantasy"*).
5. **Közvetlen könyvtárhoz adás (`+ Add to Library`):**
   - A felhasználó az ajánlókártyán lévő gombbal azonnal felveheti a címet `PlanToWatch` vagy `PlanToRead` státuszban.
   - A cím bekerül az SQLite adatbázisba, frissülnek a könyvtári számlálók, és a tétel azonnal eltűnik az aktív ajánlások közül (vagy keresési találat esetén átvált `✓ In Library` állapotjelzésre).
   - A meglévő szinkron backlog-ajánló (`GetRecommendations`) a háttérben azonnali átmenetet és kompatibilitást biztosít.
6. **Modern Discovery Hub UI integráció:**
   - **Kapszula navigáció (Discovery Pills):** `[For You]` (személyre szabott ízlésprofil), `[Popular]` (globális népszerűségi lista a `GetPopularRecommendationsAsync` és `JikanApiClient.GetPopularAsync` hívásokkal), valamint `[Anime]`, `[Manga]`, `[Manhwa]` típus szerinti szűrések.
   - **Integrált katalóguskereső:** A fejlécben elhelyezett keresősávval azonnali valós idejű Jikan keresés indítható. A keresőmező törlésekor vagy a `✕` gombra kattintva a felület zökkenőmentesen visszavált az ajánlási nézetre.
   - **Szűrőmenü:** `🎛️` gomb flyout menüvel a műfajválasztáshoz és manuális frissítéshez.

### 2.6. Adatfolyam: Biztonsági mentés (JSON Export és Import)
1. **Export:**
   - A `BackupService.ExportToFileAsync` létrehozza a `BackupEnvelope` (v1) struktúrát.
   - Ha egy tételnek van helyi borítója (`CoverImagePath`), a fizikai képfájl beolvasásra kerül, és Base64 stringként beágyazódik a JSON-be (`coverImageBase64`).
   - A JSON formázottan (`WriteIndented = true`) fájlba íródik.
2. **Import (Kétlépcsős tranzakciós modell):**
   - **1. Validáció:** `ValidateFileAsync` / `ValidateJson` ellenőrzi a szintaxist, a v1 verziót, a kötelező mezőket, az összeférhetőséget, a duplikációkat, valamint a Base64 képadatok dekódolhatóságát és magic byte-jait. A felületen megjelenik az érvényes tételek száma és a megerősítő doboz.
   - **2. Végrehajtás:** `ConfirmImportAsync` meghívja a `BackupService.ImportReplaceAsync`-ot.
   - A csomagolt képek mentésre kerülnek a `covers/` mappába egyedi GUID-dal.
   - Egyetlen SQLite tranzakció fut le: `DELETE FROM MediaItems; INSERT INTO MediaItems ...`.
   - **Rollback és takarítás:** Ha a tranzakció bármilyen okból hibát dob, az adatbázis visszagördül, és az importálás közben kiírt új képfájlok azonnal törlődnek (nem marad sem elrontott adat, sem árva fájl).

---

## 3. Kulcsfontosságú fájlok és felelősségeik

### Modellek és Adatelérés
- [`MediaItem.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Models/MediaItem.cs): A központi domain entitás, tartalmazza a mezőket (`Id`, `Title`, `Type`, `Status`, `CurrentProgress`, `TotalProgress`, `Score`, `RewatchCount`, `CoverImageUrl`, `CoverImagePath`, `Genres`, `Synopsis`, stb.) és a domain logikai metódusokat (`TrySetProgress`, `TryIncrementProgress`, `TryDecrementProgress`, `MarkCompleted`, `StartRewatch`).
- [`DatabaseInitializer.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Data/DatabaseInitializer.cs): Felelős az alkalmazás könyvtárának (`~/.local/share/AnimeTracker`) létrehozásáért, az SQLite tábla (`MediaItems`) és a részleges egyedi index inicializálásáért, valamint a séma-migrációkért (`CoverImagePath` oszlop meglétének ellenőrzése).
- [`MediaItemRepository.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Data/MediaItemRepository.cs): Dapper-alapú aszinkron CRUD réteg (`AddAsync`, `GetByIdAsync`, `GetByMalIdAsync`, `GetAllAsync`, `UpdateAsync`, `DeleteAsync`).

### Szolgáltatások (Services)
- [`MediaTrackingService.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Services/MediaTrackingService.cs): Üzleti műveletek koordinálása az adatbázissal (haladás növelése/csökkentése, közvetlen beállítása, befejezés, újranézés).
- [`CoverStorageService.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Services/CoverStorageService.cs): Helyi borítóképek biztonságos kezelése. Magic bytes ellenőrzés (PNG, JPEG, WebP), 10 MB-os méretkorlát, fájlmentés `covers/` alá, relatív és abszolút elérési utak feloldása, törlés és bájt-visszaolvasás.
- [`ImageLoader.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Services/ImageLoader.cs): Helyi fájlokból, memóriabájtokból és távoli URL-ekről aszinkron módon Avalonia `Bitmap`-et generáló segédosztály memóriacache-sel és hibabiztos fallbackkel.
- [`RecommendationService.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Services/RecommendationService.cs): Backlog-ajánló algoritmus, műfaji affinitás számítása és indoklás generálása.
- [`JikanApiClient.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Services/Jikan/JikanApiClient.cs): REST API kliens Jikan v4 végpontokhoz (`/anime`, `/manga`), hálózati hibák és sebességkorlátok (HTTP 429) kezelése.
- [`BackupService.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Services/Backup/BackupService.cs): JSON export és tranzakciós import Base64 képtámogatással és visszaállítási rollback logikával.

### ViewModel-ek (ViewModels)
- [`MainWindowViewModel.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/ViewModels/MainWindowViewModel.cs): Főképernyő állapota, szekciónavigáció, könyvtárszűrés, számlálók, ajánlások és biztonsági mentési parancsok.
- [`MediaCardViewModel.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/ViewModels/MediaCardViewModel.cs): Egyedi könyvtári kártyák nézetmodellje inline stepperekkel és aszinkron borítóbetöltéssel.
- [`RecommendationCardViewModel.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/ViewModels/RecommendationCardViewModel.cs): Ajánlási kártya indoklással, pontszámmal és borítóval.
- [`AddTitleViewModel.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/ViewModels/AddTitleViewModel.cs): Új cím felvétele (Jikan katalóguskeresés debounced hívásokkal + Kézi felvitel kezdő haladással és borítókép tallózással).
- [`MediaDetailViewModel.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/ViewModels/MediaDetailViewModel.cs): Részletező és szerkesztő modális nézet: közvetlen numerikus haladásmódosítás, borítócsere/törlés, pontozás, törlés megerősítés és `Cancel` művelet.

### Nézetek (Views)
- [`MainWindow.axaml`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Views/MainWindow.axaml) / [`.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Views/MainWindow.axaml.cs): Főablak reszponzív elrendezéssel, oldalsávval, kártyarácsal és dialógus-kezeléssel.
- [`AddTitleWindow.axaml`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Views/AddTitleWindow.axaml) / [`.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Views/AddTitleWindow.axaml.cs): Modális dialógus két füllel (Katalóguskeresés és Kézi felvitel).
- [`MediaDetailWindow.axaml`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Views/MediaDetailWindow.axaml) / [`.cs`](file:///home/janix/Projects/AnimeTracker/src/AnimeTracker/Views/MediaDetailWindow.axaml.cs): Kétoszlopos modális ablak részletek megtekintéséhez és szerkesztéséhez.

---

## 4. Adatmodell és Üzleti Szabályok

### 4.1. Típusok és Státuszok
- **Média típusok (`MediaType`):** `Anime`, `Manga`, `Manhwa`.
- **Státuszok (`MediaStatus`):**
  - Anime esetén érvényes: `Watching`, `PlanToWatch`, `Completed`, `OnHold`, `Dropped`. (*Reading* és *PlanToRead* tiltott).
  - Manga és Manhwa esetén érvényes: `Reading`, `PlanToRead`, `Completed`, `OnHold`, `Dropped`. (*Watching* és *PlanToWatch* tiltott).

### 4.2. Haladási határok és szabályok
- `CurrentProgress`: Nem lehet negatív (`>= 0`).
- `TotalProgress`: Opcionális pozitív egész szám.
- Ha `TotalProgress` ismert, a `CurrentProgress` nem haladhatja meg (`CurrentProgress <= TotalProgress`).
- Befejezett (`Completed`) állapot esetén, ha `TotalProgress` ismert, a `CurrentProgress` kötelezően megegyezik vele.

### 4.3. Értékelés (Score)
- Opcionális egész szám `1` és `10` között.
- Bármikor törölhető (`null` értékre állítható).

### 4.4. Duplikációvédelem
- **Katalógusból:** SQLite egyedi részleges index védi a duplikációt:
  ```sql
  CREATE UNIQUE INDEX IF NOT EXISTS UX_MediaItems_MalId_MalType
      ON MediaItems (MalId, MalType)
      WHERE MalId IS NOT NULL AND MalType IS NOT NULL;
  ```
- **Kézi felvitelnél:** Címegyezés esetén a rendszer először figyelmeztetést ad; a felhasználó második kattintással megerősítheti az azonos nevű cím felvételét.

### 4.5. Borítókép-kezelés
- **Támogatott formátumok:** PNG, JPEG, WebP.
- **Méretkorlát:** Maximum 10 MB fájlonként.
- **Fejléc-ellenőrzés:**
  - JPEG: `FF D8 FF`
  - PNG: `89 50 4E 47 0D 0A 1A 0A`
  - WebP: `RIFF` ... `WEBP`
- **Megjelenítési hierarchia:**
  1. Helyi egyéni borító (`CoverImagePath`), ha létezik a lemezen.
  2. Távoli katalógus borító (`CoverImageUrl`), ha meg van adva.
  3. Beépített csillag placeholder (`★`), ha nincs elérhető kép.

---

## 5. Fejlesztési és Ügynökszabályok (Agent Guidelines)

Ha a projekten dolgozol, **kivétel nélkül tartsd be az alábbi szabályokat** (lásd még: [`AGENTS.md`](file:///home/janix/Projects/AnimeTracker/AGENTS.md)):

1. **Szigorú Test-Driven Development (TDD):**
   - Nincs produktív kód teszt nélkül! Új funkció vagy bugfix esetén először a hibát/elvárást bemutató xUnit tesztet írd meg.
   - Figyeld meg a piros állapotot (`RED`), írd meg a minimális kódot a zöldhöz (`GREEN`), majd végezz refaktort (`REFACTOR`).
2. **Egyszerűség és Minimalizmus (No Overengineering):**
   - Ne vezess be DI-keretrendszert (pl. Microsoft.Extensions.DependencyInjection).
   - Ne vezess be Entity Framework Core-t vagy más nehéz ORM-et (a Dapper + SQLite tökéletesen megfelel).
   - Ne vezess be felesleges interfészeket, generikus repository rétegeket vagy plugin-rendszert.
3. **Minden I/O Aszinkron:**
   - Adatbázis, hálózat és fájlrendszer műveletek kötelezően `async`/`await` mintával működjenek.
4. **SQLite Tesztelés és Versenyhelyzetek:**
   - Az xUnit alapértelmezett párhuzamos tesztfuttatása SQLite kapcsolatoknál és a `SqliteConnection.ClearAllPools()` hívásnál zárolási és objektum-megsemmisítési hibát okozhat.
   - Ezért a tesztprojektben a párhuzamosítás le van tiltva: [`TestCollectionOrder.cs`](file:///home/janix/Projects/AnimeTracker/tests/AnimeTracker.Tests/TestCollectionOrder.cs) tartalmazza a `[assembly: CollectionBehavior(DisableTestParallelization = true)]` attribútumot. Ezt tilos eltávolítani!
   - A tesztekben a lemezre íródó ideiglenes adatbázisok a tesztosztály `Dispose` metódusában takarításra kerülnek.
5. **Minőségi Kapu a Feladat Befejezése Előtt:**
   - Bármely feladat lezárása előtt a teljes tesztcsomagnak sikeresen le kell futnia:
     ```bash
     dotnet test
     ```
   - A Release fordításnak 0 hibával és 0 figyelmeztetéssel kell lefutnia:
     ```bash
     dotnet build -c Release
     ```
6. **Kódstabilitás:**
   - Meglévő, működő kódot ne írj át indokolatlanul. Meglévő kommenteket és struktúrákat tarts tiszteletben.

---

## 6. Helyi Adatelérési Útvonalak

- **Adatbázis:** `~/.local/share/AnimeTracker/library.db`
- **Borítóképek:** `~/.local/share/AnimeTracker/covers/`
- **Git Repository:** `https://github.com/MONHuN23/AnimeTracker`
