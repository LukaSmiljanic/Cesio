---
name: cesio-adr
description: Koristi u Cesio projektu kad se donosi arhitektonska ili poslovna odluka koja ima alternative i koju će neko kasnije pitati "zašto ovako" - izbor biblioteke, model podataka, način obračuna, granice modula.
---

# Zapis arhitektonske odluke (ADR)

Svi ADR-ovi žive u jednom fajlu, `docs/decisions.md`: red u tabeli na vrhu i kratka sekcija `## ADR-NNN Naslov` ispod. Taj fajl je na engleskom.

1. Nađi sledeći slobodan broj u tabeli (`ADR-NNN`, tri cifre). Brojevi se ne menjaju i ne koriste ponovo.
2. Proveri da li postoji ADR koji ova odluka menja. **Accepted** ADR se nikad ne menja tiho: novi ADR ga zamenjuje, a u starom redu status postaje `Superseded by ADR-NNN` (ili `Accepted (… superseded by ADR-NNN)` kad se menja samo deo). Za to treba izričita instrukcija korisnika.
3. Dodaj red u tabelu i sekciju po šablonu ispod. Kratko: obično pola strane.
4. Statusi: `Accepted`, `Proposed` (podrazumevano dok korisnik ne potvrdi), `Open` (mora se odlučiti pre zavisnog taska). Ako odluka zavisi od otvorenog pravnog ili poslovnog pitanja, upiši `LQ-xx` / `BQ-xx` u kolonu "Depends on" i ostavi `Proposed`.
5. Ako odluka utiče na task, dodaj ADR u "Blocked by" tog taska u `tasks/README.md` dok je `Open`.
6. Posle izmene pokreni `doc-consistency`.

## Šablon
Red u tabeli:
```markdown
| ADR-NNN | Kratak naslov | Proposed | LQ-xx, BQ-xx |
```

Sekcija:
```markdown
## ADR-NNN Kratak naslov
**Context:** koji problem rešavamo i koja ograničenja postoje.
**Decision:** šta smo izabrali, jednom ili dvema rečenicama.
**Alternatives:** opcija A (zašto ne); opcija B (zašto ne).
**Consequences:** šta postaje lakše, šta teže, šta moramo da pratimo.
```
