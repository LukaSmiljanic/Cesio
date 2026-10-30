---
name: cesio-deploy
description: Koristi u Cesio projektu kad se menja CI/CD pipeline, Docker ili konfiguraciju servera (`deploy/`), promenljive okruženja, ili kad korisnik traži deploy na staging ili produkciju. Ne koristi za izmene aplikacionog koda.
---

# Deploy i infrastruktura

Plan infrastrukture je u `tasks/024-deployment.md` i ADR-023; server: `deploy/README.md`; arhitektura u `docs/architecture.md` §2 i §6; baza u `docs/database.md` §6–7.

## Osnovno pravilo
Na staging i produkciju kači CI pipeline, ne Claude direktno. Claude piše i menja konfiguraciju pipeline-a, a deploy se pokreće kroz pipeline (merge u granu ili tag).
Claude NIKAD ne pokreće komande koje direktno menjaju produkciju (deploy, migracije na produkcionoj bazi, brisanje resursa), čak ni kad ima pristup.

## Okruženja
| Okruženje | Kako se kači | Baza | Spoljni sistemi |
|---|---|---|---|
| lokalno | `docker compose up -d` (sqlserver, smtp4dev), pa `dotnet run --project src/Platform.Api` i `npm --prefix web run dev` | lokalni SQL Server u Docker-u | lažni klijenti / SEF demo |
| staging | merge u `main` + jedno odobrenje (task 024) | poseban server sa svojim SQL Server-om, test podaci, bez produkcionih podataka | SEF demo |
| produkcija | posle smoke testa na staging-u + ručno odobrenje u pipeline-u; okidač: tag `v*`; pipeline proverava da je commit na `main` i deployuje istu potpisanu sliku koju je staging izgradio za taj commit (bez nje deploy pada) | produkcioni SQL Server na produkcionom serveru, nedostupan spolja | SEF produkcija |

Hosting: sopstveni Linux server sa Docker-om po okruženju (ADR-023; lokacija još nije odlučena, LQ-35): `deploy/compose.yml` (API, worker, migrator, SQL Server Express, ClamAV, Caddy). Slike u GitHub Container Registry. CI: GitHub Actions (`.github/workflows/ci.yml`, `codeql.yml`, `dependency-review.yml`). Deploy: `.github/workflows/deploy.yml` preko SSH-a i `deploy/release.sh` na serveru; testovi se vrte u `ci.yml`.

## Pipeline mora da ima, ovim redom
1. Lint i provera tipova (`dotnet build` sa warnings as errors; `npm run build`, `npm run lint`, `npm run format:check`)
2. Testovi (`dotnet test`, `npm test`; integracioni sa Testcontainers i lažnim spoljnim sistemima) i provera da nema float-a (`scripts/check-no-float.sh`)
3. Build i skeniranje (gitleaks, CodeQL), potpisana Docker slika
4. Migracije baze kroz migration job, pre pokretanja nove verzije, i samo kompatibilne unazad (expand/contract, vidi `cesio-migration`)
5. Deploy
6. Smoke test posle deploya (health endpoint, login stranica, konekcija sa bazom)
7. Za produkciju: ručno odobrenje pre koraka 4

## Tajne i konfiguracija
- SEF ključevi su šifrovani u bazi (`platform.secrets`); lozinke servera (SQL, SMTP, lozinka sertifikata) su u `.env` na serveru, koji se nikad ne commituje. GitHub okruženje ima samo SSH ključ za deploy (secret) i adresu servera (promenljive). Nikad u repou.
- Postoje `.env.example` (lokalno) i `deploy/.env.example` (server) sa svim promenljivim i bez pravih vrednosti. Kad dodaš promenljivu, dodaj je i tamo.
- Staging i produkcija nikad ne dele ključeve ni bazu.

## Rollback
- Svaki deploy mora moći da se vrati na prethodnu verziju jednim korakom: `deploy/release.sh` sa digestima iz `.env.previous` na serveru.
- Zato migracije u istom deployu moraju raditi i sa prethodnom verzijom koda.

## Kad korisnik traži "okači"
1. Proveri da je radna grana čista i da testovi prolaze lokalno.
2. Reci korisniku šta će se desiti: koja okruženja, koje migracije, da li ima rušećih izmena.
3. Pripremi merge, PR ili tag po tabeli okruženja. Za produkciju stani i čekaj potvrdu korisnika pre bilo kog koraka.
