---
name: cesio-external-integration
description: Koristi u Cesio projektu kad pišeš ili menjaš kod koji komunicira sa spoljnim sistemom - SEF (sistem e-faktura), CEF, APR, NBS, servisi za potpis ili screening. Ne koristi za interne module.
---

# Rad sa eksternim sistemima

## Struktura
- Svaki spoljni sistem ima svoj projekat u `src/Integrations/Integrations.<Sistem>/` (postoje `Integrations.Sef`, `Integrations.Cef`, `Integrations.Screening`, `Integrations.Signatures`).
- Moduli zavise od interfejsa, ne od konkretnog klijenta, da bi testovi koristili lažnu implementaciju.
- Ključevi i URL-ovi dolaze iz konfiguracije (lokalno), odnosno šifrovano iz baze (`platform.secrets`, ADR-023), nikad iz koda. Odvojeno za demo i produkciju (SEF ima demo okruženje).
- **SEF klijent je samo za čitanje:** nema metoda koje šalju, menjaju ili otkazuju dokumente.

## Obavezno za svaki poziv
1. **Timeout:** eksplicitan, nikad podrazumevani beskonačni.
2. **Retry:** samo za privremene greške (mreža, 5xx, 429), sa eksponencijalnim čekanjem i ograničenim brojem pokušaja.
3. **Idempotentnost:** operacije koje menjaju stanje nose ključ koji sprečava duplo izvršenje.
4. **Validacija odgovora:** odgovor spoljnog sistema se proverava pre upotrebe. Neočekivan format je greška, ne prazna vrednost.
5. **Logovanje:** zahtev i odgovor se loguju BEZ ličnih podataka, PIB-a, brojeva računa i ključeva (`Masking`).
6. **Mapiranje grešaka:** greške spoljnog sistema se prevode u domenske greške (`Error` / `Result`) sa jasnom porukom.

## Testovi
- Unit testovi sa lažnim klijentom.
- Bar jedan test za timeout, jedan za grešku i jedan za neočekivan odgovor.
- Integracioni test protiv demo okruženja, označen da se ne pokreće u svakom CI pokretanju.

## Dokumentacija
Pre izmene pročitaj odgovarajuću sekciju u `docs/integrations.md` (§1 SEF, §2 CEF, §3 APR, §4 NBS, §7 e-potpis). Kad nešto nije jasno u dokumentaciji spoljnog sistema, pozovi `legal-review` u režimu "External system research".
