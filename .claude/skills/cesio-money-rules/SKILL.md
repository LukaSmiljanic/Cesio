---
name: cesio-money-rules
description: Koristi u Cesio projektu kad god pišeš ili menjaš kod koji računa ili čuva novac - diskont, proviziju platforme, iznose faktura i ponuda, prijavu isplate, naknade, zaokruživanje, RSD/EUR. Ne koristi za prikaz teksta bez računanja.
---

# Pravila za novac u Cesio projektu

## Neprekršiva pravila
1. Novac se NIKAD ne čuva ni ne računa u `float`/`double`/`real`. Koristi `decimal` u C#, `decimal(19,4)` u SQL Server-u (ADR-008, `docs/database.md` §1).
2. Svaki iznos ima valutu: `Money(decimal Amount, Currency Currency)` iz `src/Shared/Shared.Kernel/Money.cs`, ne goli broj. U bazi preko `HasMoney(...)` (`src/Shared/Shared.Infrastructure/Persistence/Converters.cs`). U API-ju iznos je string (`docs/api.md` §1), nikad JSON broj.
3. Zaokruživanje je na jednom mestu: `Money.Round()` u `src/Shared/Shared.Kernel/Money.cs`. Način: half away from zero (`MidpointRounding.AwayFromZero`) na minor units valute (2 decimale za RSD i EUR). Zaokružuje se samo konačni rezultat obračuna naknade i prikaz.
4. RSD i EUR se ne sabiraju i ne porede (`CurrencyMismatchException`). Konverzija, ako se ikad uvede, nosi kurs, izvor kursa i datum kursa. MVP je samo RSD (ADR-009, Proposed).
5. Svaka promena iznosa ide u audit log u istoj transakciji: ko, kada, stara vrednost, nova vrednost, razlog. Finansijski redovi se ne menjaju; ispravke su kompenzacioni redovi (INV-F3).
6. Platforma nikad ne prima, ne drži i ne prosleđuje novac posla (ADR-004). Ona samo beleži prijavu isplate i potvrdu prijema.

## Pojmovi (iste oznake kao `docs/financial-model.md` §1)
| Oznaka | Značenje |
|---|---|
| `F` | nominala potraživanja (`faceValue`) |
| `P` | otkupna cena iz ponude (`purchasePrice`), iznos koji faktor isplaćuje ustupiocu |
| `D` | diskont, zarada faktora po ponudi: `D = F − P` |
| `s` | udeo platforme u diskontu: **15%** (ADR-007, BQ-01, product owner 2026-10-22) |
| `PF` | provizija platforme (`computedPlatformFee`) |

## Formula (ADR-007)
```
D  = F − P             (ponuđena zarada faktora; INV-F1: 0 < P < F, ista valuta)
PF = Round(D × s)      (provizija platforme; nikad veća od D)
```
- Osnovica je **ponuđeni diskont**, ne nominala, ne isplaćeni iznos i ne stvarno naplaćeni iznos (platforma naplatu od dužnika ne vidi, ADR-004). Zato je `D > 0` uvek, pa negativna provizija ne postoji.
- Naknada se izračuna i snimi u ponudi (`feeScheduleSnapshot`, `computedPlatformFee`), a dospeva kad posao pređe u `Funded`: tačno jedan `FeeCharge` po poslu (INV-F3). Posao otkazan pre `Funded` nema naknadu.
- Udeo, opcioni minimum i maksimum i platilac (podrazumevano faktor) su konfiguracija u fee schedule-u (task 017a), ne konstanta u kodu.
- Povraćaj naknade posle `Funded` (regres, neplaćanje): podrazumevano nema povraćaja (BQ-05).
- [LQ] Dozvoljenost naknade kao procenta diskonta: LQ-03, radna pretpostavka 2026-10-22, pitanje otvoreno.
- [LQ] PDV na proviziju platforme: LQ-43, otvoreno. Iznos naknade se računa bez PDV-a; PDV je odvojen i konfigurabilan.

Implementacija formule živi SAMO u modulu `src/Modules/Fees/Modules.Fees/` (taskovi 017a i 018). Ostali kod je poziva preko `Modules.Fees.Contracts`, ne ponavlja je.

## Obavezni test primeri
Svaka izmena obračuna mora proći ove slučajeve (s = 15%, bez minimuma i maksimuma).

| F | P | D | Očekivano PF |
|---|---|---|---|
| 1.000.000,00 RSD | 970.000,00 RSD | 30.000,00 | 4.500,00 RSD |
| 333.333,33 RSD | 323.333,33 RSD | 10.000,00 | 1.500,00 RSD |
| 100.000,00 RSD | 99.876,67 RSD | 123,33 | 18,50 RSD (18,4995 → half away from zero) |
| 100.000,00 RSD | 99.999,99 RSD | 0,01 | 0,00 RSD (0,0015 → 0,00) |
| 10.000,00 EUR | 9.800,00 EUR | 200,00 | 30,00 EUR |
| 100.000,00 EUR | 97.000,00 EUR | 3.000,00 | 450,00 EUR (primer iz `docs/business-model.md`) |
| `P ≥ F`, `P ≤ 0` ili različite valute | | | greška (INV-F1, `CurrencyMismatchException`) |

## Posle izmene
Pozovi `money-reviewer` agenta sa opisom izmene i listom izmenjenih fajlova.
