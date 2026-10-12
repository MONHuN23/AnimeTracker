# AnimeTracker – Agent Guidelines

## 1. Technológiai alapok
- **Nyelv és futtatókörnyezet:** C# / .NET 10 LTS
- **UI keretrendszer:** Avalonia UI 12.x
- **Architektúra minta:** MVVM (`CommunityToolkit.Mvvm`)
- **Adattárolás:** Helyi SQLite adatbázis (`Microsoft.Data.Sqlite`, Dapper micro-ORM, tranzakciókezelés; EF Core használata kizárva)
- **Külső API:** Jikan API v4 (REST)
- **Tesztkeretrendszer:** xUnit automatizált tesztek

## 2. Kódminőség és architektúra
- **Egyszerűség és karbantarthatóság:** Az alkalmazás méretéhez illő, közvetlen és tiszta architektúra. Kerüld az overengineeringet.
- **Következetes MVVM és aszinkronitás:** Világos szétválasztás a felület (Views), a nézetmodellek (ViewModels) és az adatelérés (Services / Repositories) között. Minden adatbázis- és hálózati I/O művelet aszinkron (`async`/`await`).
- **Nincsenek felesleges absztrakciók:** Ne hozz létre szükségtelen interfészeket, rétegeket, generikus keretrendszereket vagy pluginarchitektúrát. Interfészt csak akkor vezess be, ha teszteléshez vagy tényleges cserélhetőséghez indokolt.
- **Minimális függőségek:** Ne vezess be DI-keretrendszert vagy új külső könyvtárat konkrét, igazolt indok nélkül.
- **Kódstabilitás:** Meglévő, működő kódot ne írj át indokolatlanul.

## 3. Tesztelés és ellenőrzés
- **Automatizált tesztek:** A kritikus üzleti logikát (haladási határok, státuszváltás, újranézési számláló, duplikációvédelem, ajánló heurisztika, JSON export/tranzakciós import) xUnit tesztekkel ellenőrizd.
- **TDD:** Implementáció előtt kövesd a Superpowers `test-driven-development` folyamatát (teszt először, majd minimális működő kód, végül refaktor).
- **Szisztematikus hibakeresés:** Hibák és elakadások esetén kövesd a `systematic-debugging` megközelítést; tilos találgatás alapján javítani.
- **Bizonyíték a lezárás előtt:** Befejezett feladatot csak tényleges teszt- és buildellenőrzés után tekints késznek (`verification-before-completion`).
- **Minőségi kapu:** A munka befejezése előtt a releváns teszteknek sikeresen le kell futniuk (`dotnet test`), és a Release buildnek hibamentesen kell fordulnia (`dotnet build -c Release`).

## 4. Munkafolyamat
- **Skillek alkalmazása:** Kövesd a Superpowers releváns skilljeit az egyes munkafázisokban.
- **Vezérfonal:** A jóváhagyott MVP specifikáció és az implementációs terv a fejlesztés kizárólagos alapja.
- **Kis, ellenőrizhető lépések:** Egyszerre mindig egy fókuszált feladaton dolgozz, ellenőrizd, és csak utána lépj a következőre.
- **Egyszerű alapértelmezések:** Ha egy implementációs részlet nincs kifejezetten előírva, mindig a legegyszerűbb, a specifikációval harmonizáló megoldást válaszd.
