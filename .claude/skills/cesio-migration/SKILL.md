---
name: cesio-migration
description: Koristi u Cesio projektu kad menjaš šemu baze ili podatke u njoj - nova tabela ili kolona, promena tipa, preimenovanje, prebacivanje postojećih zapisa. Ne koristi za upite koji samo čitaju.
---

# Izmena baze

Baza je SQL Server (SQL Server u kontejneru na serveru, ADR-021, ADR-023). Svaki modul ima svoju šemu i svoj `DbContext` izveden iz `ModuleDbContext`, i svoje migracije (ADR-003, `docs/database.md`). Konvencije (snake_case, `decimal(19,4)`, `datetimeoffset(3)`, `version`) primenjuje `ModuleDbContext` automatski.

## Koraci
1. Napravi migraciju alatom projekta (EF Core, lokalni alat iz `dotnet-tools.json`). Nikad ne menjaj šemu ručno.
   ```
   dotnet tool restore
   dotnet ef migrations add <Naziv> --project <projekat modula> --context <Modul>DbContext
   ```
   Primer za `platform` šemu: `--project src/Shared/Shared.Infrastructure --context PlatformDbContext`. Design-time factory postoji, pa startup projekat nije potreban.
   Folder migracija i komanda migration runner-a: [POPUNITI: posle taska 003, stavka 8 "per-module migrations folder; migration runner console command"].
2. Svaka migracija ima ispravan `Down()`, osim kad je to nemoguće. Tada to napiši na vrhu migracije i objasni zašto.
3. Ne menjaj migraciju koja je već primenjena u deljenom okruženju. Napravi novu.
4. Rušeće izmene (brisanje ili preimenovanje kolone) radi u dva koraka (expand/contract): prvo dodaj novo i prebaci kod, pa tek u sledećoj migraciji ukloni staro.
5. Kolone za novac koriste `Money` preko `HasMoney(...)` (`decimal(19,4)` + `char(3)` + CHECK za valutu), po `cesio-money-rules`. CI proverava da nema `float`/`real`/`money`.
6. Invarijante koje drži baza (filtrirani unique indeksi INV-R1…R3, CHECK konstrainti) se ne slabe.

## Audit i finansijski redovi su nepromenljivi
- Tabele `audit.*` i finansijski redovi se ne menjaju unazad: ne brišu se redovi i ne prepisuju se stare vrednosti. Aplikaciona uloga ima samo `INSERT, SELECT` nad `audit.*` (`docs/audit.md` §1).
- Ako se menja format audita, novi zapisi koriste novi format, stari ostaju kakvi jesu.

## Provera
1. Pokreni migraciju na praznoj bazi (`docker compose up -d`, lokalni SQL Server).
2. Pokreni je na bazi sa test podacima (seed).
3. Pokreni rollback (`dotnet ef database update <PrethodnaMigracija> ...`) i ponovo migraciju.
4. Pokreni `dotnet test` (integracioni testovi koriste Testcontainers SQL Server i primenjuju migracije).
