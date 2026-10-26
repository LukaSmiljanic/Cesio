---
name: cesio-deploy
description: Koristi u Cesio projektu kad se menja CI/CD pipeline, Docker ili infrastrukturna konfiguracija (Bicep), promenljive okruženja, ili kad korisnik traži deploy na staging ili produkciju. Ne koristi za izmene aplikacionog koda.
---

# Deploy i infrastruktura

Plan infrastrukture je u `tasks/024-deployment.md`; arhitektura u `docs/architecture.md` §2 i §6; baza u `docs/database.md` §6–7.

## Osnovno pravilo
Na staging i produkciju kači CI pipeline, ne Claude direktno. Claude piše i menja konfiguraciju pipeline-a, a deploy se pokreće kroz pipeline (merge u granu ili tag).
Claude NIKAD ne pokreće komande koje direktno menjaju produkciju (deploy, migracije na produkcionoj bazi, brisanje resursa), čak ni kad ima pristup.

## Okruženja
| Okruženje | Kako se kači | Baza | Spoljni sistemi |
|---|---|---|---|
| lokalno | `docker compose up -d` (sqlserver, azurite, smtp4dev), pa `dotnet run --project src/Platform.Api` i `npm --prefix web run dev` | lokalni SQL Server u Docker-u | lažni klijenti / SEF demo |
| staging | merge u `main` + jedno odobrenje (task 024) | odvojena Azure SQL baza, test podaci, bez produkcionih podataka | SEF demo |
| produkcija | posle smoke testa na staging-u + ručno odobrenje u pipeline-u; okidač: tag `v*`; pipeline proverava da je commit na `main` i promoviše sliku koju je staging izgradio za taj commit (bez nje deploy pada) | produkciona Azure SQL baza (privatni endpoint: još nije urađen, task 024) | SEF produkcija |

Hosting: Azure (EU region, ADR-016 Proposed, LQ-35): Azure Container Apps (API, worker, migration job), Azure SQL Database, Storage, Key Vault, Container Registry, Application Insights; infrastruktura u Bicep-u. CI: GitHub Actions (`.github/workflows/ci.yml`, `codeql.yml`, `dependency-review.yml`). Deploy: `.github/workflows/deploy.yml` i `deploy-environment.yml` (task 024); testovi se vrte u `ci.yml`.

## Pipeline mora da ima, ovim redom
1. Lint i provera tipova (`dotnet build` sa warnings as errors; `npm run build`, `npm run lint`, `npm run format:check`)
2. Testovi (`dotnet test`, `npm test`; integracioni sa Testcontainers i lažnim spoljnim sistemima) i provera da nema float-a (`scripts/check-no-float.sh`)
3. Build i skeniranje (gitleaks, CodeQL), potpisana Docker slika
4. Migracije baze kroz migration job, pre pokretanja nove verzije, i samo kompatibilne unazad (expand/contract, vidi `cesio-migration`)
5. Deploy
6. Smoke test posle deploya (health endpoint, login stranica, konekcija sa bazom)
7. Za produkciju: ručno odobrenje pre koraka 4

## Tajne i konfiguracija
- Ključevi (SEF, potpis, e-mail) žive u Azure Key Vault-u; aplikacije koriste managed identity i Entra autentifikaciju za Azure SQL. Pipeline se prijavljuje na Azure preko OIDC federacije i nema tajni, samo promenljive GitHub okruženja. Nikad u repou ni u `.env` koji se commituje.
- Postoji `.env.example` sa svim promenljivim i lažnim vrednostima. Kad dodaš promenljivu, dodaj je i tamo.
- Staging i produkcija nikad ne dele ključeve ni bazu.

## Rollback
- Svaki deploy mora moći da se vrati na prethodnu verziju jednim korakom: aktiviranje prethodne Container Apps revizije (single revision mode, task 024).
- Zato migracije u istom deployu moraju raditi i sa prethodnom verzijom koda.

## Kad korisnik traži "okači"
1. Proveri da je radna grana čista i da testovi prolaze lokalno.
2. Reci korisniku šta će se desiti: koja okruženja, koje migracije, da li ima rušećih izmena.
3. Pripremi merge, PR ili tag po tabeli okruženja. Za produkciju stani i čekaj potvrdu korisnika pre bilo kog koraka.
