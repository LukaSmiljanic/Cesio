# Cesio — Pitanja za advokata

> Verzija 1.0 · 2026-10-20
> Ovaj dokument je napravljen iz registra [legal-questions.md](legal-questions.md) i sadrži samo pravna pitanja. Poreska pitanja (LQ-43, LQ-44, LQ-45, LQ-46, LQ-69) šaljemo posebno poreskom savetniku.
> Uz svako pitanje je oznaka iz registra (LQ-xx), da bismo odgovor mogli da upišemo na pravo mesto.

## Ukratko o projektu

**Cesio** je internet platforma (marketplace) za faktoring u Srbiji:

- **Prodavci** su firme i preduzetnici koji imaju nenaplaćene fakture prema drugim firmama. Na platformi nude te fakture na otkup.
- **Faktori** su banke i društva za faktoring koja imaju saglasnost za rad. Oni daju ponude da otkupe fakturu.
- Prodavac bira najbolju ponudu. Faktor i prodavac zatim potpisuju ugovor o faktoringu, a faktor uplaćuje novac **direktno prodavcu**.
- Platforma **ne otkupljuje fakture, ne daje kredite i nikada ne prima niti prosleđuje novac.** Samo povezuje strane, proverava podatke, vodi tok posla i čuva dokaze.
- Platforma zarađuje naknadom po uspešnom poslu. Odluka product ownera (2026-10-22): 15 % zarade faktora (razlike između iznosa fakture i cene koju je ponudio), bez minimuma i maksimuma; plaća je faktor.

## Pojmovi koje koristimo

| Pojam | Značenje |
|-------|----------|
| SEF | Sistem elektronskih faktura Ministarstva finansija |
| CEF | Centralna evidencija faktoringa (čl. 32a Zakona o faktoringu); **još ne radi** |
| APR | Agencija za privredne registre, uključujući Registar faktoringa i Centralnu evidenciju stvarnih vlasnika (CESV) |
| KYB / CDD | Provera firme (ko je, ko je zastupa, ko su stvarni vlasnici) / provera klijenta po propisima o sprečavanju pranja novca |
| QES | Kvalifikovani elektronski potpis |
| Pristupnica | Dokument kojim zastupnik firme prijavljuje firmu na platformu i prihvata uslove |
| Regres | Pravo faktora da naplatu traži od prodavca ako dužnik ne plati |
| P0 / P1 / P2 | Hitnost: **P0** = bez odgovora ne možemo da pokrenemo platformu; **P1** = blokira jednu funkciju; **P2** = potrebno kasnije, pre rasta |

## Kako da odgovorite

Za svako pitanje molimo:
- kratak odgovor;
- izvor i obrazloženje (propis, član, praksa);
- ime advokata i datum;
- da li predlažete da nešto promenimo u planu.

Ako radite u fazama, molimo prvo ova pitanja, od kojih zavisi da li je model uopšte izvodljiv: **1, 2, 5, 8, 14, 22, 23, 31, 53, 67** (isti skup kao u fajlu kljucna-pitanja-za-advokata.txt). Zatim ostala pitanja **P0** (pre svega 3, 10, 15, 32, 36 i 68), pa P1 i P2. (Prvom krugu pripada i pitanje PDV-a na našu naknadu, LQ-43, koje šaljemo poreskom savetniku.) Uz pitanja tražimo i nacrte dokumenata, navedene na kraju.

---

## A. Status platforme i osnivanje

### 1. Da li nam treba dozvola za rad platforme? (LQ-01 · P0)
**Šta želimo:** platformu koja samo povezuje prodavce i faktore. Ne bismo otkupljivali fakture niti dirali novac.
**Pitanje:** Da li za takvu platformu treba dozvola, saglasnost ili upis kod Komisije za hartije od vrednosti, NBS ili Ministarstva finansija? Da li je to „posredovanje u faktoringu”, i da li je ta delatnost regulisana?

### 2. Gde je granica između platforme i obavljanja faktoringa? (LQ-02 · P0)
**Šta želimo:** da platforma ponudi standardne uslove, rangira ponude po iznosu koji prodavac dobija i čuva potpisane ugovore.
**Pitanje:** Može li se zbog ovih funkcija smatrati da platforma sama obavlja faktoring? Koje funkcije bi prešle tu granicu?

### 3. Da li da tražimo mišljenje regulatora? (LQ-53 · P0)
**Šta želimo:** sigurnost pre pokretanja. U Srbiji već postoje slične platforme, npr. SEF LINK, koja zahtev šalje većem broju partnerskih faktora.
**Pitanje:** Treba li tražiti pisano mišljenje Komisije za HoV i/ili Ministarstva finansija o statusu platforme? Kakav je postupak, koliko traje i koliko je mišljenje obavezujuće? Govori li postojanje sličnih platformi išta o stavu regulatora?

### 4. Kako registrovati firmu koja vodi platformu? (LQ-54 · P1)
**Šta želimo:** da firma bude registrovana kao tehnološka kompanija, ne kao finansijska institucija.
**Pitanje:** Koja šifra delatnosti odgovara, a da ne upućuje na finansijsku delatnost? Ako se platforma ipak smatra posrednikom (pitanje 1), postoje li zahtevi za kapital, upravu ili podobnost?

## B. Tok novca

### 5. Da li ostajemo van propisa o platnim uslugama? (LQ-06 · P0)
**Šta želimo:** faktor uplaćuje novac direktno na račun prodavca iz svoje banke. Na platformi faktor samo prijavi uplatu, a prodavac potvrdi da je novac stigao.
**Pitanje:** Potvrdite da ovaj model ostaje van Zakona o platnim uslugama.

### 6. Smemo li ikada pokretati plaćanja? (LQ-07 · P1)
**Šta želimo:** kasnije, radi lakšeg rada, generisati gotov nalog za plaćanje (NBS IPS QR kod), koji platilac samo potvrdi u svojoj banci. Prvo za plaćanje naših faktura za naknadu, a možda i za uplatu faktora prodavcu.
**Pitanje:** Može li platforma bez dozvole generisati takve naloge ili pružati usluge iniciranja plaćanja? Da li se odgovor razlikuje za ta dva slučaja?

### 7. Šta ako bismo uveli escrow račun? (LQ-08 · P2)
**Šta želimo:** u budućnosti možda poseban račun preko koga ide novac, radi veće sigurnosti strana.
**Pitanje:** Da li bi escrow ili račun za naplatu (naš ili partnerske banke) zahtevao dozvolu ili poseban ugovor sa bankom?

## C. Naknada platforme

### 8. Da li je naša naknada dozvoljena? (LQ-03 · P0)
**Šta želimo:** naplatu procenta od zarade faktora na poslu, samo kada je posao uspešan i novac isplaćen. Zarada je razlika između iznosa fakture i cene koju je faktor ponudio. Primer: faktura 100.000, faktor plaća 97.000, zarada 3.000; uz udeo od 15 %, naknada je 450. Plaća faktor, a možda i prodavac.
**Pitanje:** Da li je takva naknada dozvoljena? Može li se, zato što učestvujemo u zaradi faktora, smatrati da i sami učestvujemo u obavljanju faktoringa (vidi pitanje 2)? Postoje li ograničenja, obaveza obelodanjivanja ili pravila o sukobu interesa? Platforma zarađuje više kada je cena za prodavca lošija.

## D. Registracija, nalog i ovlašćenja

### 9. Da li su naši Uslovi korišćenja punovažni? (LQ-26 · P0)
**Šta želimo:** firma prihvata Uslove korišćenja klikom i potpisom pristupnice. Prodavac ima jedan zajednički nalog, koji može koristiti npr. njegov računovođa, a firma odgovara za sve što se uradi kroz nalog.
**Pitanje:** Da li su uslovi prihvaćeni klikom punovažni prema pravnim licima? Šta moraju da sadrže (ograničenje odgovornosti, poverljivost, merodavno pravo, nadležnost)? Da li je punovažna odredba da firma odgovara za sve radnje kroz zajednički nalog?

### 10. Ko sme da prihvati ponudu i potpiše ugovor u ime firme? (LQ-61 · P0)
**Šta želimo:** ponudu prihvata i ugovor potpisuje zastupnik upisan u APR ili osoba sa punomoćjem. Platforma to proverava.
**Pitanje:** Koja lica to smeju? U kojoj formi mora biti punomoćje (pisano, potpisano elektronskim potpisom, overeno)? Kako zajednički nalog utiče na dokazivanje ko je radnju zaista preduzeo? (Povezano sa pitanjima 9 i 11.)

### 11. Kako proveriti identitet zastupnika na daljinu? (LQ-32 · P1)
**Šta želimo:** registraciju bez dolaska u kancelariju. Zastupnik potpisuje pristupnicu kvalifikovanim elektronskim potpisom, ili šalje skeniranu ručno potpisanu pristupnicu koju potvrdimo preko službenog emaila firme iz APR-a.
**Pitanje:**
- Koji način identifikacije je prihvatljiv: kvalifikovani sertifikat, video identifikacija (ko je sme koristiti), ili eID.gov.rs visokog nivoa pouzdanosti?
- Koje podatke (naziv firme, PIB, MB, JMBG) upisuju u sertifikat Pošta, Halcom, E-Smart i MUP (lična karta), i koji sertifikat dokazuje vezu osobe sa firmom?
- Da li je prihvatljiva skenirana pristupnica potvrđena preko emaila iz APR-a?

### 12. Smemo li koristiti eID.gov.rs za prijavu korisnika? (LQ-52 · P2)
**Šta želimo:** kasnije omogućiti prijavu na platformu preko državnog eID sistema.
**Pitanje:** Može li privatna platforma koristiti eID.gov.rs za prijavu ili identifikaciju korisnika? Pod kojim uslovima i uz kakav ugovor sa Kancelarijom za IT i eUpravu? (Povezano sa pitanjima 11 i 44.)

### 13. Na kom jeziku moraju biti pravni tekstovi? (LQ-62 · P1)
**Šta želimo:** tekstove na srpskom, latinicom, a možda i englesku verziju za strane banke.
**Pitanje:** Moraju li Uslovi korišćenja, politika privatnosti i izjave biti na srpskom (Zakon o službenoj upotrebi jezika i pisma)? Koja verzija ima prednost ako postoji i engleska? Da li je latinica dovoljna?

## E. Provera firmi i sprečavanje pranja novca

### 14. Da li je platforma obveznik po Zakonu o sprečavanju pranja novca? (LQ-28 · P0)
**Šta želimo:** proveru firmi (ko je, ko je zastupa, ko su vlasnici) radi sigurnosti tržišta. Pravnu obavezu CDD provere ostavljamo faktorima, koji su obveznici.
**Pitanje:** Da li je platforma obveznik po nekoj kategoriji iz čl. 4 Zakona o sprečavanju pranja novca i finansiranja terorizma?

### 15. Mogu li se faktori osloniti na našu proveru firme? (LQ-29 · P0)
**Šta želimo:** faktoru, uz saglasnost prodavca, damo paket podataka o firmi (KYB paket), da ne mora sve da prikuplja ponovo.
**Pitanje:** Mogu li se faktori pravno osloniti na podatke koje prikupi platforma? Ako ne mogu, koji je pravni osnov za deljenje paketa i kakva saglasnost prodavca je potrebna?

### 16. Koliku proveru firme moramo da radimo? (LQ-30 · P0)
**Šta želimo:** jednostavnu registraciju. Proveravamo APR podatke, zastupnike, stvarne vlasnike i sankcione liste.
**Pitanje:** Koji minimum provere platforma mora ili treba da sprovodi: identifikaciju zastupnika, stvarne vlasnike (CESV), funkcionere (PEP)?

### 17. Koje sankcione liste da proveravamo? (LQ-31 · P1)
**Šta želimo:** automatsku proveru firmi, zastupnika i vlasnika na sankcionim listama.
**Pitanje:** Koje liste su obavezne (srpske, UN)? Da li banke i faktori očekuju i liste EU i SAD (OFAC)?

### 18. Šta radimo kada posumnjamo na nešto? (LQ-33 · P1)
**Šta želimo:** kada sistem uoči sumnjivu aktivnost, posao se zaustavlja i ide na ručnu proveru.
**Pitanje:** Ako platforma nije obveznik, sme li ili mora da obavesti faktore ili nadležne organe? Kakav je rizik da time „otkrijemo” sumnju osobi na koju se odnosi?

## F. Provera faktora

### 19. Kako da proverimo da faktor ima saglasnost za rad? (LQ-09 · P0)
**Šta želimo:** da ponude daju samo banke i društva za faktoring sa važećom saglasnošću. Proveravamo ih u APR Registru faktoringa i na NBS spisku banaka, a odobravaju ih dva naša službenika.
**Pitanje:** Koji javni izvor je merodavan otkako je nadležnost prešla na Komisiju za HoV? Važe li saglasnosti koje je izdalo Ministarstvo finansija i dalje, bez ikakve radnje? Da li je upis ili potvrda APR-a dovoljan dokaz, i koliko brzo se nove saglasnosti Komisije upisuju u registar?

### 20. Mogu li strani faktori da učestvuju? (LQ-11 · P1)
**Šta želimo:** možda kasnije omogućiti i stranim faktorima da daju ponude.
**Pitanje:** Mogu li strani faktori bez saglasnosti u Srbiji otkupljivati domaća potraživanja preko platforme?

### 21. Da li je za banke korišćenje platforme eksternalizacija? (LQ-65 · P1)
**Šta želimo:** da banke koriste platformu kao kanal za nove klijente.
**Pitanje:** Da li banke i društva za faktoring korišćenje platforme smatraju eksternalizacijom (npr. po odluci NBS za banke)? Ako da, koje odredbe moramo da prihvatimo u ugovoru: pravo revizije, lokaciju podataka, plan izlaska, podizvođače kao što je Microsoft Azure?

## G. SEF i uvoz faktura

### 22. Sme li prodavac da nam da svoj SEF API ključ? (LQ-21 · P0)
**Šta želimo:** prodavac nam da pristupni ključ za SEF, a mi automatski preuzimamo njegove fakture i proveravamo da ih je kupac prihvatio. Tako smanjujemo rizik da neko ponudi izmišljenu fakturu.
**Pitanje:** Da li je to dozvoljeno po pravilima SEF-a? Da li je potrebno formalno ovlašćenje prodavca?

### 23. Kakva je naša odgovornost što ključ omogućava i brisanje faktura? (LQ-51 · P0)
**Šta želimo:** ključ koristimo samo za čitanje. SEF, međutim, nema ključ samo za čitanje: isti ključ omogućava i storniranje, otkazivanje i brisanje faktura (provereno 2026-10-13).
**Pitanje:** Kakva odgovornost i obaveze zaštite iz toga slede za platformu? Šta moraju sadržati ovlašćenje prodavca i naši uslovi?

### 24. Može li se faktura koja je ustupljena stornirati u SEF-u? (LQ-22 · P1)
**Šta želimo:** da otkrijemo ako prodavac stornira fakturu koju je već prodao faktoru.
**Pitanje:** Ima li SEF (ili planira) oznaku za ustupljene fakture? Potvrdite pravilo da se faktura koja je predmet faktoringa ne može stornirati ili otkazati, i navedite član.

### 25. Da li je prihvatanje fakture u SEF-u priznanje duga? (LQ-23 · P0)
**Šta želimo:** na platformu primamo samo fakture koje je kupac u SEF-u prihvatio, kao dokaz da je posao stvaran.
**Pitanje:** Da li je status „prihvaćena” u SEF-u pravno priznanje duga od strane kupca (dužnika)?

### 26. Smemo li primati fakture prema javnom sektoru? (LQ-16 · P1)
**Šta želimo:** na početku samo dužnike iz privatnog sektora, a kasnije i javna preduzeća i ustanove.
**Pitanje:** Da li su fakture prema javnom sektoru prihvatljive za faktoring preko platforme, i kako se to odnosi na Centralni registar faktura?

## H. Fakture i izjave prodavca

### 27. Do kog roka dospeća faktura može biti predmet faktoringa? (LQ-10 · P0)
**Šta želimo:** da primamo samo fakture kojima rok plaćanja još nije prošao, sa ograničenjem najdužeg roka (sada 180 dana, može se menjati).
**Pitanje:** Šta tačno znači „kratkoročno” potraživanje u Zakonu o faktoringu (najduži rok)? Da li je potraživanje kome je rok prošao ikada prihvatljivo?

### 28. Mogu li dva faktora otkupiti delove iste fakture? (LQ-12 · P1)
**Šta želimo:** na početku se ustupa samo cela faktura, a kasnije možda i delovi velikih faktura različitim faktorima.
**Pitanje:** Da li je delimično ustupanje različitim faktorima dozvoljeno, s obzirom na zabranu iz čl. 22 Zakona o faktoringu?

### 29. Koje garancije prodavac treba da da, i gde? (LQ-40 · P0)
**Šta želimo:** prodavac uz svaku fakturu izjavljuje:
- da je roba isporučena ili usluga pružena, odnosno da potraživanje postoji;
- da ugovor sa kupcem ne zabranjuje ustupanje;
- da nema prigovora ni prebijanja;
- da faktura nije ranije ustupljena.
**Pitanje:** Koje od ovih garancija treba da budu u uslovima platforme, a koje u ugovoru o faktoringu? Da li nešto nedostaje?

### 30. Kako ustupiti fakturu u evrima koja se plaća u dinarima? (LQ-50 · P1)
**Šta želimo:** na početku samo fakture u dinarima, a kasnije i fakture sa iznosom vezanim za evro.
**Pitanje:** Kako se kod takvih faktura pravno izražavaju ustupljeni iznos i kupoprodajna cena?

## I. Oglasi i ponude

### 31. Smemo li podatke o dužniku pokazati većem broju faktora? (LQ-36 · P0)
**Šta želimo:** faktori na oglasu vide ko je dužnik (naziv, PIB) i podatke o fakturi, da bi mogli da procene rizik. Prodavac bira da li oglas vide svi faktori ili samo neki.
**Pitanje:** Smeju li ti podaci biti prikazani većem broju faktora, s obzirom na poslovnu tajnu i moguće klauzule o poverljivosti između prodavca i dužnika? Da li je dovoljna saglasnost prodavca? Da li su to poslovne tajne po Zakonu o zaštiti poslovne tajne, i koje mere treba da imamo (obaveza poverljivosti u Uslovima korišćenja za faktore, vodeni žig, evidencija pristupa)?

### 32. Kakva je pravna priroda ponude faktora? (LQ-60 · P0)
**Šta želimo:**
- Faktor daje obavezujuću ponudu koja se ne može menjati, ima rok važenja i može se povući pre prihvatanja.
- Posle prihvatanja faktor još mora da uradi svoju zakonsku proveru klijenta (CDD) i može odustati ako ona ne prođe.
**Pitanje:** Šta je po Zakonu o obligacionim odnosima takva ponuda: ponuda, poziv na ponudu ili uslovna ponuda? Da li je punovažna „obavezujuća ponuda pod uslovom CDD provere”? Odgovara li faktor ako odustane? Kako pravno funkcionišu rok važenja i povlačenje? (Povezano sa pitanjem 36.)

### 33. Smemo li porediti ponude i prikazivati godišnji trošak? (LQ-04 · P1)
**Šta želimo:** prodavcu prikazati ponude jednu pored druge, istaći onu po kojoj dobija najviše novca i prikazati okvirni godišnji trošak svake ponude.
**Pitanje:** Može li nas to učiniti finansijskim savetnikom ili kreditnim posrednikom? Platforma naplaćuje faktoru procenat diskonta, pa zarađuje više kada je cena za prodavca lošija (pitanje 8). Da li to utiče na odgovor? Moramo li prodavcu, u Uslovima korišćenja ili pri poređenju ponuda, obelodaniti da faktor plaća naknadu i kako se ona računa?

### 34. Postoji li gornja granica cene faktoringa? (LQ-58 · P1)
**Šta želimo:** faktori slobodno određuju cenu (diskont), a platforma samo prikazuje ponude.
**Pitanje:** Postoje li zakonska ograničenja diskonta (zelenaški ugovor, čl. 141 ZOO, ili posebno pravilo za faktoring)? Može li platforma odgovarati za prikaz ili omogućavanje ponude koju sud kasnije oceni kao zelenašku?

### 35. Da li postoje rizici po zaštitu konkurencije? (LQ-55 · P1)
**Šta želimo:**
- Faktori ne vide tuđe ponude, ali bismo im kasnije mogli prikazivati zbirne podatke (npr. koliko često pobeđuju, raspon cena).
- Prvim partnerima bismo možda dali period bez naknade ili ekskluzivitet.
**Pitanje:** Postoji li rizik nedozvoljene razmene informacija između konkurenata? Da li su ekskluzivitet i posebni uslovi za prve partnere problem? Šta ako platforma postane dominantna u ovoj niši?

## J. Ugovor, potpis i ustupanje

### 36. Kada je posao obavezujući i kada faktura prelazi na faktora? (LQ-25 · P0)
**Šta želimo:** prodavac prihvata ponudu na platformi, a zatim obe strane potpisuju ugovor o faktoringu.
**Pitanje:** Da li prihvatanje ponude na platformi može biti obavezujući predugovor, ili obavezuje tek potpisan ugovor? U kom trenutku potraživanje prelazi na faktora: potpisom, isplatom ili obaveštenjem dužnika?

### 37. Koji potpis je potreban? (LQ-24 · P0)
**Šta želimo:** ugovor o faktoringu potpisuje se kvalifikovanim elektronskim potpisom (QES) van platforme, a platforma proverava potpise. Izjave uz fakture prodavac potvrđuje na platformi.
**Pitanje:** Koji nivo potpisa je potreban za ugovor o faktoringu, a koji za izjave prodavca: kvalifikovani, napredni ili prihvatanje klikom?

### 38. Smemo li ponuditi standardni ugovor o faktoringu? (LQ-27 · P1)
**Šta želimo:** na početku svaki faktor koristi svoj ugovor, a kasnije možda jedan standardni obrazac na platformi.
**Pitanje:** Sme li platforma da ponudi standardni obrazac ugovora? Ko odgovara za njegov sadržaj?

### 39. Može li se raditi preko okvirnog ugovora? (LQ-59 · P1)
**Šta želimo:** faktor i prodavac jednom potpišu okvirni ugovor, a svaku sledeću fakturu ustupaju jednostavnom potvrdom na platformi. Tako rade Finspot i hrvatska FINA.
**Pitanje:** Da li je to moguće? Šta mora sadržati specifikacija za svaku fakturu i koji nivo potpisa joj treba? (Povezano sa pitanjem 37.)

### 40. Šta moramo znati o regresu? (LQ-13 · P0)
**Šta želimo:** faktor u ponudi bira da li je faktoring sa regresom ili bez njega, a platforma beleži ishod posla.
**Pitanje:** Šta ugovor obavezno sadrži u svakom od ta dva slučaja? Ima li platforma ikakvu obavezu u vezi sa regresom (čl. 16, obaveštenje u roku od 8 dana)?

### 41. Da li su obaveštenja kroz platformu pravno valjana? (LQ-63 · P1)
**Šta želimo:** da strane jedna drugu obaveštavaju kroz aplikaciju i emailom: o prihvatanju, otkazu, raskidu ili regresu.
**Pitanje:** Da li su takva obaveštenja pravno valjana između strana i od platforme? Mogu li Uslovi korišćenja te kanale odrediti kao ugovoreni način dostavljanja?

## K. Centralna evidencija faktoringa i obaveštenje dužnika

### 42. Kako se obaveštava dužnik? (LQ-14 · P0)
**Šta želimo:** posle potpisivanja dužnik se obaveštava da plaća faktoru, a ne prodavcu. Dokaz o obaveštenju čuvamo na platformi.
**Pitanje:** Ko obaveštava dužnika, u kojoj formi i kada? Kada CEF počne da radi, da li upis u CEF zamenjuje obaveštenje? Sme li platforma da šalje obaveštenje u ime prodavca ili faktora?

### 43. Kada tačno faktura mora biti upisana u CEF? (LQ-17 · P0)
**Šta želimo:** da vodimo strane kroz upis u CEF kada sistem počne da radi.
**Pitanje:** Zakon kaže da ustupilac upisuje fakturu „odmah po evidentiranju u SEF-u, a najkasnije narednog radnog dana”. Šta to znači za fakture koje se ustupaju nedeljama posle izdavanja?

### 44. Smemo li upisivati fakture u CEF umesto korisnika? (LQ-18 · P1)
**Šta želimo:** da platforma sama, automatski, upiše ustupanje u CEF.
**Pitanje:** Sme li platforma da upisuje u ime korisnika, s obzirom da CEF traži prijavu preko eID-a visokog nivoa?

### 45. Kakvo je pravno dejstvo upisa u CEF? (LQ-19 · P1)
**Šta želimo:** da znamo na šta se faktori mogu osloniti.
**Pitanje:** Šta upis u CEF znači za prvenstvo između više faktora, za oslobađanje dužnika od obaveze i za ništavost kasnijih ugovora o istoj fakturi?

### 46. Šta moramo reći faktorima dok CEF ne radi? (LQ-20 · P0)
**Šta želimo:** u produkciju idemo tek kada CEF počne da radi. Ipak, možda bismo ranije napravili zatvoreni probni rad.
**Pitanje:** Pre početka rada CEF-a ne možemo da proverimo da li je faktura već prodata nekome van platforme. Šta moramo da kažemo faktorima o tom riziku?

## L. Prevare, sporovi i odgovornost

### 47. Za šta odgovara platforma? (LQ-39 · P0)
**Šta želimo:** jasno ograničenu odgovornost platforme.
**Pitanje:** Kolika je naša odgovornost u ovim slučajevima:
- verifikovana faktura se pokaže lažnom;
- faktoru se oduzme saglasnost usred posla;
- tehnička greška: prekid rada, nedostupnost SEF-a, propušten rok, pogrešan rezultat provere.
Da li je ograničenje odgovornosti moguće i punovažno?

### 48. Da li važe pravila za elektronsku trgovinu? (LQ-57 · P1)
**Šta želimo:** da znamo koje obaveze imamo kao internet servis.
**Pitanje:** Da li smo pružalac usluga informacionog društva po Zakonu o elektronskoj trgovini? Koje informacije moramo objaviti na sajtu i u Uslovima korišćenja? Možemo li se pozvati na izuzeće od odgovornosti za dokumente i izjave koje unose korisnici?

### 49. Kakva je uloga platforme u sporovima? (LQ-41 · P1)
**Šta želimo:** da platforma beleži sporove i pomaže stranama da ih reše, ali da ne presuđuje.
**Pitanje:** Kakvu ulogu da preuzmemo (nikakvu, olakšavanje ili medijaciju)? Koje pravo i koji sud, ili arbitražu, da predvidimo?

### 50. Šta smemo da uradimo kad posumnjamo na prevaru? (LQ-42 · P1)
**Šta želimo:** da zamrznemo oglas ili posao i obavestimo faktora.
**Pitanje:** Šta smemo, a šta moramo da uradimo kada sumnjamo na prevaru ili dvostruko finansiranje: zamrzavanje, obaveštavanje faktora, prijavu?

### 51. Da li nam treba osiguranje? (LQ-68 · P2)
**Šta želimo:** da budemo prihvatljivi bankama i faktorima.
**Pitanje:** Da li je osiguranje od profesionalne odgovornosti ili sajber osiguranje obavezno po zakonu ili uobičajeno očekivanje banaka? Kakvo pokriće je uobičajeno za ovakvog posrednika?

## M. Podaci o ličnosti, bezbednost i čuvanje podataka

### 52. Ko je rukovalac podacima o ličnosti? (LQ-34 · P0)
**Šta želimo:** da podatke o zastupnicima i kontakt osobama delimo sa faktorima kada je potrebno.
**Pitanje:** Ko je rukovalac, a ko obrađivač: platforma ili faktor? Ovo se odnosi na podatke o zastupnicima prodavaca i na kontakt osobe dužnika.

### 53. Smemo li čuvati podatke u Azure-u u EU? (LQ-35 · P0)
**Šta želimo:** da platforma radi u Microsoft Azure-u, u nekom od EU regiona.
**Pitanje:** Da li je prenos podataka u EU dozvoljen po ZZPL i da li su potrebne standardne ugovorne klauzule? Moraju li se finansijski podaci čuvati u Srbiji?

### 54. Da li nam trebaju lice za zaštitu podataka i procena uticaja? (LQ-37 · P1)
**Šta želimo:** usklađenost sa ZZPL od prvog dana.
**Pitanje:** Da li moramo imati lice za zaštitu podataka (DPO), procenu uticaja (DPIA) i evidenciju radnji obrade?

### 55. Smemo li beležiti IP adresu i uređaj? (LQ-38 · P1)
**Šta želimo:** da za svaku važnu radnju beležimo IP adresu i uređaj, radi dokaza i sprečavanja prevara.
**Pitanje:** Koji je pravni osnov za to i koliko dugo smemo da čuvamo te podatke?

### 56. Šta ako dođe do bezbednosnog incidenta? (LQ-64 · P1)
**Šta želimo:** spreman postupak za slučaj curenja podataka ili napada.
**Pitanje:** Koje su obaveze obaveštavanja o povredi podataka (Povereniku i licima, rokovi)? Da li smo operator IKT sistema od posebnog značaja po Zakonu o informacionoj bezbednosti (npr. kao deo infrastrukture finansijskog tržišta), sa obavezom akta o bezbednosti i prijave incidenata?

### 57. Kolačići, analitika i email poruke (LQ-66 · P1)
**Šta želimo:** analitiku korišćenja aplikacije, povremena obaveštenja korisnicima o novim funkcijama i marketinške poruke.
**Pitanje:** Šta od toga zahteva pristanak i u kom obliku (Zakon o elektronskim komunikacijama, ZZPL)? Smemo li slati obaveštenja o proizvodu i marketinške poruke na lične poslovne adrese korisnika bez prethodnog pristanka?

### 58. Smemo li koristiti zbirne podatke o poslovima? (LQ-67 · P2)
**Šta želimo:** izveštaje i uporedne podatke za faktore (npr. prosečna cena po roku dospeća), a možda i javne izveštaje o tržištu.
**Pitanje:** Smemo li koristiti zbirne ili anonimizovane podatke u te svrhe? Šta moraju da sadrže Uslovi korišćenja, i kako se to odnosi na poslovnu tajnu (pitanje 31) i zaštitu konkurencije (pitanje 35)?

### 59. Koliko dugo moramo da čuvamo podatke? (LQ-47 · P0)
**Šta želimo:** da čuvamo sve što zakon traži i ne duže od toga.
**Pitanje:** Koji su rokovi čuvanja za: poslove i ugovore, podatke o proveri firmi, audit zapise, fakture i naše fakture za naknade (po Zakonu o računovodstvu, Zakonu o elektronskom fakturisanju i AML propisima)?

### 60. Šta ako korisnik traži brisanje podataka? (LQ-48 · P1)
**Šta želimo:** da poštujemo prava korisnika, ali da sačuvamo ono što zakon traži.
**Pitanje:** Kako uskladiti pravo na brisanje sa obaveznim čuvanjem podataka?

### 61. Da li moramo koristiti kvalifikovano elektronsko čuvanje? (LQ-70 · P1)
**Šta želimo:** potpisane ugovore i dokaze čuvamo kod sebe, zaštićene od izmena (digitalni otisci i zapisi koji se ne mogu menjati).
**Pitanje:** Da li to čuva njihovu dokaznu snagu, ili moramo koristiti pružaoca usluge kvalifikovanog elektronskog čuvanja (zakon o elektronskom dokumentu)? Da li se primenjuju i propisi o arhivskoj građi?

## N. Marketing i brend

### 62. Šta smemo da tvrdimo u reklamama? (LQ-56 · P1)
**Šta želimo:** da oglašavamo platformu porukama kao što su „novac za 24 sata” ili „najbolja cena”, i da se poredimo sa bankama.
**Pitanje:** Koje tvrdnje su ograničene po Zakonu o oglašavanju i pravilima za finansijske usluge? Moramo li isticati da nismo faktor ni kreditor?

### 63. Možemo li zaštititi naziv „Cesio”? (LQ-71 · P2)
**Šta želimo:** registrovan žig i domene za naziv platforme.
**Pitanje:** Može li se „Cesio” registrovati kao žig u Srbiji, a po potrebi i u EU? Postoje li sukobi sa postojećim žigovima u finansijskim uslugama? Šta je sa domenima?

## O. Kasnije faze

### 64. Šta ako bismo i sami otkupljivali fakture? (LQ-05 · P2)
**Šta želimo:** za sada ništa. To je moguća daleka opcija.
**Pitanje:** Da li bi otkup faktura za sopstveni račun zahtevao posebnu saglasnost za faktoring i razdvajanje radi sukoba interesa?

### 65. Obrnuti faktoring (LQ-15 · P1)
**Šta želimo:** kasnije omogućiti velikim kupcima da svoje dobavljače uvedu na platformu, da bi ovi brže dobili novac.
**Pitanje:** Postoje li dodatni pravni zahtevi za obrnuti faktoring, kada posao pokreće dužnik?

### 66. Poslovanje sa inostranstvom (LQ-49 · P2)
**Šta želimo:** kasnije strane prodavce, izvozne fakture van SEF-a i potraživanja u evrima.
**Pitanje:** Koja pravila važe za prekogranične poslove, uključujući devizne propise?

## P. Dodato naknadno

### 67. Da li su registracija i Uslovi korišćenja dovoljni prema faktorima? (LQ-72 · P0)
**Šta želimo:** sa faktorima, ni sa bankama ni sa društvima za faktoring, ne zaključujemo poseban ugovor. Faktor se registruje, prolazi proveru saglasnosti za rad i prihvata Uslove korišćenja za faktore. Njima su uređeni pravila rada, naknada, poverljivost, odgovornost, zaštita podataka i sporovi.
**Pitanje:** Da li je to pravno dovoljno za obavezu plaćanja naknade, obavezu poverljivosti podataka sa oglasa, ograničenje odgovornosti i zaštitu podataka? Da li bankama i društvima za faktoring njihovi propisi (npr. pravila NBS o eksternalizaciji) ipak nalažu pisani ugovor sa platformom? (Povezano sa pitanjima 9, 21 i 31.)

### 68. Ko u ime faktora prihvata Uslove korišćenja? (LQ-73 · P0)
**Šta želimo:** da prihvatanje Uslova za faktore bude jednostavno, a ipak punovažno za banku ili društvo za faktoring.
**Pitanje:** Ko u ime faktora sme da prihvati Uslove: samo zastupnik upisan u APR, ili i zaposleni sa punomoćjem? Da li je dovoljno prihvatanje klikom sa naloga, ili je potreban dokument koji potpisuje zastupnik (kao pristupnica kod prodavca), i u kojoj formi (pisano, QES)? (Povezano sa pitanjima 9, 10 i 67.)

### 69. Možemo li menjati Uslove korišćenja za faktore? (LQ-74 · P1)
**Šta želimo:** da Uslove, pa i visinu naknade, možemo menjati kako se platforma razvija.
**Pitanje:** Može li platforma jednostrano menjati Uslove za faktore, posebno visinu naknade? Pod kojim uslovima izmena obavezuje faktora koji je ranije prihvatio Uslove: rok obaveštenja, izričito ponovno prihvatanje, pravo na raskid? Da li se izmena primenjuje na ponude i poslove započete pre izmene? (Povezano sa pitanjima 9 i 67.)

---

## Dokumenti koje vas molimo da pripremite
1. Uslovi korišćenja, za prodavce i za faktore.
2. U Uslovima za faktore posebno obaveza poverljivosti i obaveza plaćanja naknade. Sa faktorima ne zaključujemo poseban ugovor (pitanje 67).
3. Pristupnica prodavca.
4. Izjave prodavca uz svaku fakturu.
5. Politika privatnosti, evidencija radnji obrade i ulazni podaci za procenu uticaja.
6. Saglasnost za deljenje podataka o firmi faktorima i za korišćenje SEF API ključa.
7. Obaveštenje faktorima o riziku dvostrukog ustupanja van platforme (pitanje 46).

Molimo i preporuku da li da tražimo mišljenje regulatora (pitanje 3), kao i ponudu sa procenom obima posla, rokom i redosledom rada.
