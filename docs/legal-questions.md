# Cesio — Pravna i poreska pitanja

> Verzija 1.2 · 2026-10-22 · Za advokatsku kancelariju i poreskog savetnika.
> Ovo je **jedini registar** pravnih pitanja projekta (ID-jevi LQ-xx). Ostala dokumentacija na njih upućuje samo po ID-ju.
> Ovo nije pravno mišljenje. Svi pravni zaključci u našoj dokumentaciji su privremeni dok ih ne potvrdite pisanim putem.

**Pokretanje produkcije je blokirano** dok sva pitanja **P0** nemaju pisani odgovor, unet u [regulatory.md](regulatory.md) i [decisions.md](decisions.md).

## 1. Šta tražimo
1. **Pisano mišljenje** za sva pitanja označena **P0** (blokiraju pokretanje), sa izvorima (propis, član, praksa). Zatim P1 i P2.
2. **Nacrte dokumenata:**
   - Uslovi korišćenja za prodavce i za faktore;
   - posebno u Uslovima za faktore: obaveza poverljivosti i obaveza plaćanja naknade (sa faktorima se ne zaključuje poseban ugovor, ADR-022);
   - Pristupnica prodavca;
   - Izjave prodavca uz svaku fakturu;
   - Politika privatnosti, evidencija radnji obrade i ulazni podaci za procenu uticaja (DPIA);
   - Saglasnost za deljenje KYB paketa i za korišćenje SEF API ključa;
   - Obaveštenje faktorima o riziku dvostrukog ustupanja van platforme.
3. **Preporuku** da li i kako tražiti mišljenje Komisije za hartije od vrednosti ili Ministarstva finansija (LQ-53).
4. **Ponudu:** procenu obima posla, rok i redosled.

Pitanja označena *(porez)* idu poreskom savetniku ili računovođi.

**Format odgovora po pitanju:** odgovor · izvor i obrazloženje · ime advokata · datum · da li predlažete izmenu modela.

## 2. Kako platforma radi (činjenice potrebne za odgovore)
- **Šta je Cesio:** B2B tehnološka platforma. Pravna lica i preduzetnici iz Srbije (prodavci) nude nedospela potraživanja iz elektronskih faktura iz SEF-a. **Samo banke i društva za faktoring sa saglasnošću** (faktori) daju ponude. Prodavac bira ponudu.
- **Platforma ne otkupljuje potraživanja, ne odobrava kredite i ne prima, ne drži i ne prosleđuje novac.** Faktor plaća kupoprodajnu cenu direktno na račun prodavca. Platforma samo beleži prijavu isplate i potvrdu prijema.
- **Prihod:** naknada po uspešnom poslu (ADR-007, odluka product ownera 2026-10-22; dozvoljenost je pitanje LQ-03): 15 % zarade faktora na poslu, odnosno razlike između iznosa fakture i cene koju je faktor ponudio, bez minimuma i maksimuma; plaća je faktor posle potvrde isplate, na osnovu posebne fakture platforme izdate kroz SEF. Sistem podržava i to da plaća prodavac ili da se naknada deli.
- **Fakture:** samo e-fakture iz SEF-a koje je kupac (dužnik) prihvatio. Uvoze se preko SEF API ključa prodavca. MVP: samo RSD, samo ustupanje celog potraživanja, samo dužnici iz privatnog sektora u Srbiji.
- **Prodavac (MVP, radna pretpostavka, ADR-020 „Proposed”):** jedan zajednički nalog po firmi; firma odgovara za sve radnje kroz nalog. PIB se potvrđuje važećim SEF ključem. Zastupnik upisan u APR (ili punomoćnik) potpisuje **pristupnicu**: QES potpisan PDF ili skenirana ručno potpisana pristupnica potvrđena preko službenog emaila iz APR-a. Račun za isplatu mora biti upisan na PIB/MB firme u registru računa NBS.
- **Faktor:** pristupa **po pozivu**. Registruje se na platformi i prihvata Uslove korišćenja za faktore; poseban ugovor sa platformom se ne zaključuje (ADR-022, prihvaćen 2026-10-22 na osnovu radne pretpostavke; pravna dovoljnost je pitanje LQ-72). Verifikuje se preko APR Registra faktoringa ili NBS spiska banaka, uz odobrenje dva službenika usklađenosti. Može imati više korisnika. **Faktor sam sprovodi CDD/KYC** nad prodavcem. Platforma mu, uz saglasnost prodavca, daje KYB paket.
- **Oglas:** verifikovani faktori vide puni identitet dužnika (naziv, PIB, MB) i podatke o fakturi. Vide ga svi verifikovani faktori ili samo oni koje prodavac izabere, a prodavac pojedine faktore može isključiti.
- **Tok posla:**
  1. objava oglasa;
  2. obavezujuće ponude faktora: ne mogu se menjati, imaju rok važenja i mogu se povući pre prihvatanja;
  3. prihvatanje: radi ga član naloga označen kao potpisnik (prodavčev jedini nalog to postaje kada mu se prihvati pristupnica), uz dodatnu MFA potvrdu;
  4. CDD faktora (faktor može odbiti);
  5. ugovor faktora, uz potvrdu ključnih uslova od obe strane;
  6. potpisivanje kvalifikovanim elektronskim potpisom (QES) van platforme, a platforma proverava potpise;
  7. CEF evidencija;
  8. obaveštenje dužnika;
  9. isplata i potvrda prijema;
  10. ishod po dospeću (naplata, regres ili nenaplata).
- **CEF:** platforma se **neće pokrenuti u produkciji dok Centralna evidencija faktoringa ne počne sa radom** (radna pretpostavka, ADR-019 „Proposed”). Do tada se sve razvija i testira na SEF demo okruženju.
- **Tehnika:** Microsoft Azure u EU regionu, nepromenljiv audit trag, beleženje IP adrese i uređaja.

**Prilozi koje šaljemo:** [regulatory.md](regulatory.md) (naše razumevanje propisa, sa izvorima), [product-spec.md](product-spec.md) §5–7, [business-model.md](business-model.md) §2, [decisions.md](decisions.md) (ADR-004, 015, 018, 019, 020, 022), [market-analysis.md](market-analysis.md).

## 3. Pitanja po delovima aplikacije
Prioritet: **P0** blokira pokretanje · **P1** blokira jednu funkciju · **P2** pre rasta ili proširenja.

### 3.1 Status platforme i osnivanje
| ID | P | Pitanje |
|----|---|---------|
| LQ-01 | P0 | Da li za rad platforme koja povezuje prodavce i faktore treba dozvola, saglasnost ili upis (Komisija za HoV, NBS, Ministarstvo finansija)? Da li je to „posredovanje u faktoringu” i da li je ta delatnost regulisana? *Radna pretpostavka (product owner, 2026-10-22):* za rad platforme kao posrednika nije potrebna dozvola, saglasnost ni upis kod Komisije za HoV, NBS ili Ministarstva finansija; razvoj nastavlja na toj osnovi. Pitanje ostaje otvoreno do pisanog odgovora advokata. |
| LQ-02 | P0 | Može li se platforma smatrati da „obavlja faktoring” (npr. zbog standardizacije uslova, rangiranja ponuda ili čuvanja ugovora)? Koje funkcije bi prešle tu granicu? *Radna pretpostavka (product owner, 2026-10-22):* platforma se ne smatra da obavlja faktoring, a funkcije opisane u §2 ne prelaze tu granicu; razvoj nastavlja na toj osnovi. Pitanje ostaje otvoreno do pisanog odgovora advokata. |
| LQ-53 | P0 | Treba li pre pokretanja tražiti pisano mišljenje Komisije za HoV i/ili Ministarstva finansija o statusu platforme? Kakav je postupak, koliko traje i koliko je mišljenje obavezujuće? U Srbiji već rade slične platforme, npr. SEF LINK, koja zahtev šalje većem broju partnerskih faktora ([market-analysis.md](market-analysis.md)). Govori li njihovo postojanje išta o stavu regulatora? |
| LQ-54 | P1 | Koja šifra delatnosti odgovara tehnološkoj platformi a da ne upućuje na finansijsku delatnost? Postoje li zahtevi za kapital, upravu ili podobnost ako se platforma ipak smatra posrednikom (vidi LQ-01)? |

### 3.2 Tok novca i plaćanja
| ID | P | Pitanje |
|----|---|---------|
| LQ-06 | P0 | Potvrdite da model u kome **faktor plaća prodavcu direktno**, a platforma samo beleži potvrde, ostaje van Zakona o platnim uslugama. *Radna pretpostavka (product owner, 2026-10-22):* model u kome faktor plaća prodavcu direktno ostaje van Zakona o platnim uslugama; razvoj nastavlja na toj osnovi. Pitanje ostaje otvoreno do pisanog odgovora advokata. |
| LQ-07 | P1 | Može li platforma bez dozvole ikada pokretati plaćanja, npr. kao usluga iniciranja plaćanja ili generisanjem NBS IPS QR naloga? Razlikuje li se odgovor za plaćanje naših faktura za naknadu i za uplatu faktora prodavcu? |
| LQ-08 | P2 | Da li bi escrow ili račun za naplatu (platforme ili partnerske banke) zahtevao dozvolu ili poseban ugovor sa bankom? |

### 3.3 Naknada platforme i porezi
| ID | P | Pitanje |
|----|---|---------|
| LQ-03 | P0 | Da li je dozvoljena naknada po uspešnom poslu, koju plaća faktor i/ili prodavac? Naknada se računa kao procenat zarade faktora, odnosno razlike između iznosa fakture i cene koju je faktor ponudio (diskonta), a ne kao procenat iznosa fakture. Da li se zbog toga što platforma učestvuje u zaradi faktora može smatrati da platforma učestvuje u obavljanju faktoringa (vidi LQ-02)? Postoje li ograničenja, obaveze obelodanjivanja ili pravila o sukobu interesa, s obzirom da platforma zarađuje više kada je cena za prodavca lošija? *Radna pretpostavka (product owner, 2026-10-22):* naknada koju plaća faktor, 15 % ponuđenog diskonta, dozvoljena je i ne čini platformu učesnikom u obavljanju faktoringa; razvoj nastavlja na toj osnovi. Pretpostavka ne pokriva obelodanjivanje naknade i sukob interesa prema prodavcu (LQ-04), PDV (LQ-43) ni nastanak poreske obaveze (LQ-44). Pitanje ostaje otvoreno do pisanog odgovora advokata. |
| LQ-43 | P0 | *(porez)* Da li naknada podleže PDV-u od 20 % ili je oslobođena kao posredovanje u finansijskim uslugama (Zakon o PDV, čl. 25)? Menja li se odgovor ako naknadu plaća prodavac umesto faktora? Menja li se odgovor kada se naknada računa kao procenat diskonta faktora (njegovog prihoda od faktoringa), a ne kao procenat iznosa fakture? Da li se tada naknada smatra posredovanjem u finansijskoj usluzi (čl. 25 Zakona o PDV)? |
| LQ-44 | P0 | *(porez)* Naknadu fakturišemo kroz SEF. Kada nastaje poreska obaveza: u trenutku potvrde isplate ili nekom drugom? |
| LQ-45 | P1 | *(porez)* Priznavanje prihoda od naknada; postupanje kod otkazanih ili storniranih poslova (knjižna odobrenja). |
| LQ-46 | P1 | *(porez)* Porez po odbitku ili obrnuto obračunavanje PDV-a ako su faktori ili prodavci strana lica (kasnija faza). |
| LQ-69 | P2 | *(porez)* Da li digitalna platforma koja posreduje u B2B poslovima ima obavezu izveštavanja Poreske uprave o korisnicima ili transakcijama? |

### 3.4 Registracija, nalog i ovlašćenja
| ID | P | Pitanje |
|----|---|---------|
| LQ-26 | P0 | Da li su Uslovi korišćenja prihvaćeni klikom punovažni prema pravnim licima? Šta moraju da sadrže (ograničenje odgovornosti, poverljivost, merodavno pravo, nadležnost)? Da li je punovažna odredba da firma odgovara za sve radnje preduzete kroz njen zajednički nalog (ADR-020)? *Radna pretpostavka (product owner, 2026-10-22):* Uslovi korišćenja prihvaćeni klikom punovažni su prema pravnim licima; razvoj nastavlja na toj osnovi. Pretpostavka ne pokriva obavezan sadržaj Uslova ni odredbu o odgovornosti firme za radnje kroz zajednički nalog (ADR-020). Pitanje ostaje otvoreno do pisanog odgovora advokata. |
| LQ-61 | P0 | Ko sme da prihvati ponudu i potpiše ugovor o faktoringu u ime firme: zastupnik upisan u APR ili punomoćnik? U kojoj formi mora biti punomoćje (pisano, QES potpisano, overeno)? Kako zajednički nalog (ADR-020) utiče na dokazivanje ko je radnju preduzeo? (vidi LQ-26 i LQ-32) |
| LQ-75 | P1 | Kako platforma treba da utvrdi da je kvalifikovani elektronski potpis na ugovoru o faktoringu dao zastupnik upisan u APR: da li je dovoljno poklapanje imena i prezimena iz sertifikata sa imenom zastupnika, ili je potreban i lični identifikator (JMBG ili drugi broj iz sertifikata)? Kako postupiti kada je ime u sertifikatu napisano ćirilicom, a u APR latinicom (ili obrnuto)? Platforma iz sertifikata dobija ime (subject) i eventualno serijski ili lični broj, a u APR-u su zastupnici evidentirani imenom i JMBG-om. Sme li platforma da obrađuje i čuva JMBG iz sertifikata u svrhu poređenja sa zastupnikom? Ko snosi rizik ako potpisnik sa istim imenom nije zastupnik? (vidi LQ-24, LQ-32 i LQ-61) |
| LQ-32 | P1 | Daljinska identifikacija zastupnika: kvalifikovani sertifikat, video identifikacija (ko je sme koristiti) ili eID.gov.rs visokog nivoa? Koje identifikatore (naziv firme, PIB, MB, JMBG) svaki izdavalac QES-a (Pošta, Halcom, E-Smart, lična karta MUP) upisuje u sertifikat, i koji tip sertifikata dokazuje vezu sa pravnim licem? Da li je prihvatljiva skenirana ručno potpisana pristupnica potvrđena preko službenog emaila iz APR-a? |
| LQ-52 | P2 | Može li privatna platforma koristiti **eID.gov.rs** za prijavu ili identifikaciju korisnika? Pod kojim uslovima i uz kakav ugovor sa Kancelarijom za IT i eUpravu? (vidi LQ-32, a za pristup CEF-u LQ-18) |
| LQ-62 | P1 | Moraju li Uslovi korišćenja, politika privatnosti, izjave i drugi pravni tekstovi biti na srpskom jeziku (Zakon o službenoj upotrebi jezika i pisma)? Ako postoji i engleska verzija, koja ima prednost? Da li je latinica dovoljna? |
| LQ-72 | P0 | Sa faktorima (bankama i društvima za faktoring) ne zaključujemo poseban ugovor. Faktor se samo registruje i prihvata Uslove korišćenja za faktore (ADR-022). Da li su registracija i Uslovi korišćenja pravno dovoljni prema faktorima za obavezu plaćanja naknade, obavezu poverljivosti podataka sa oglasa, ograničenje odgovornosti i zaštitu podataka? Da li bankama i društvima za faktoring njihovi propisi (npr. pravila NBS o eksternalizaciji) ipak nalažu pisani ugovor sa platformom? (vidi LQ-26, LQ-36, LQ-65) *Radna pretpostavka (product owner, 2026-10-22):* registracija i prihvatanje Uslova korišćenja za faktore pravno su dovoljni za obavezu plaćanja naknade, poverljivost, ograničenje odgovornosti i zaštitu podataka, i poseban pisani ugovor nije potreban; razvoj nastavlja na toj osnovi. Pretpostavka ne pokriva odredbe koje bi faktori morali da traže zbog eksternalizacije (LQ-65), mere zaštite poslovne tajne (LQ-36), ko prihvata Uslove (LQ-73) ni izmene Uslova (LQ-74). Pitanje ostaje otvoreno do pisanog odgovora advokata. |
| LQ-73 | P0 | Ko u ime faktora (banke ili društva za faktoring) sme da prihvati Uslove korišćenja za faktore (ADR-022): samo zastupnik upisan u APR, ili i zaposleni sa punomoćjem? Da li je dovoljno prihvatanje klikom sa naloga, ili je potreban dokument koji potpisuje zastupnik (kao pristupnica kod prodavca), i u kojoj formi (pisano, QES)? (vidi LQ-26, LQ-61, LQ-72) |
| LQ-74 | P1 | Može li platforma jednostrano menjati Uslove korišćenja za faktore, posebno visinu naknade (ADR-007)? Pod kojim uslovima izmena obavezuje faktora koji je ranije prihvatio Uslove: rok obaveštenja, izričito ponovno prihvatanje nove verzije, pravo na raskid? Da li se izmena primenjuje na ponude i poslove započete pre izmene? (vidi LQ-26, LQ-72) |
| LQ-76 | P0 | Pristupnicu prodavca (ADR-020) generiše platforma iz podataka firme, a potpisuje zakonski zastupnik ili punomoćnik. Koji tekst pristupnica mora da sadrži da bi obavezivala firmu i pokrila: korišćenje SEF API ključa (ključ tehnički omogućava i storniranje i brisanje faktura, vidi LQ-51), saglasnost za deljenje KYB paketa sa faktorom, prihvatanje Uslova korišćenja, isplatu na račun firme i izjavu o stvarnim vlasnicima? Treba li da sadrži mesto, datum, svojstvo potpisnika i podatke o punomoćju? Sme li tekst biti bez dijakritika (č, ć, đ, š, ž), ili je potrebna latinica sa dijakriticima ili ćirilica (vidi LQ-62)? Do odgovora pristupnica sadrži samo podatke i oznaku da tekst čeka pravnu verifikaciju (task 007a). |
| LQ-77 | P1 | Da li je klik na jednokratni link poslat na službeni email firme iz APR-a (važi 72 sata; otvara ga svako ko ima pristup sandučetu) valjana potvrda volje firme i dovoljan dokaz za: (a) prihvatanje skenirane pristupnice, (b) promenu SEF ključa, (c) promenu računa za isplatu i (d) reset dvofaktorske prijave? Ako nije, šta treba dodati (npr. QES ili pismo na adresu sedišta)? Koji se dokazi o slanju i potvrdi (vreme, IP adresa) moraju čuvati i koliko dugo (vidi LQ-32, LQ-38, LQ-47 i LQ-63)? |
| LQ-78 | P1 | Sme li platforma da zatvori nacrt registracije korisnika kada drugi nalog važećim SEF ključem dokaže isti PIB (ili kada nacrt nije potvrđen u roku od 7 dana), da mu poruči samo „firma je već registrovana, obratite se njenom administratoru” i da napravi interno upozorenje kada dva različita korisnika imaju važeći SEF ključ za isti PIB? Šta o tome moraju da kažu Uslovi korišćenja, i mora li platforma takav slučaj nekome da prijavi (vidi LQ-26, LQ-42 i LQ-51)? |

### 3.5 KYB, sprečavanje pranja novca i sankcije
| ID | P | Pitanje |
|----|---|---------|
| LQ-28 | P0 | Da li je platforma obveznik po nekoj kategoriji iz čl. 4 Zakona o sprečavanju pranja novca i finansiranja terorizma? |
| LQ-29 | P0 | Mogu li se faktori osloniti na KYB podatke koje prikupi platforma (oslanjanje na treće lice)? Ako ne mogu, koji je pravni osnov i kakva saglasnost je potrebna za deljenje KYB paketa? |
| LQ-30 | P0 | Koji minimum KYB provere platforma mora ili treba da sprovodi: identifikacija zastupnika, stvarni vlasnici (CESV), funkcioneri (PEP)? |
| LQ-31 | P1 | Koje sankcione liste proveravati: srpske i UN liste? Da li banke i faktori očekuju i liste EU i OFAC? |
| LQ-33 | P1 | Šta platforma sme ili mora da uradi kada uoči sumnjivu aktivnost, ako nije obveznik: obavestiti faktore ili organe? Kakav je rizik od „otkrivanja” (tipping-off)? |

### 3.6 Verifikacija faktora
| ID | P | Pitanje |
|----|---|---------|
| LQ-09 | P0 | Koji javni izvor je merodavan za proveru saglasnosti društva za faktoring posle prelaska nadležnosti na Komisiju za HoV? Ostaju li saglasnosti Ministarstva finansija važeće bez ikakve radnje? *Istraživanje 2026-10-17:* APR Registar faktoringa (čl. 10) je javan i pretraživ (izvor u regulatory.md S9). Ostaje pitanje: da li je upis ili potvrda APR-a sam po sebi dovoljan dokaz, i koliko brzo se nove saglasnosti Komisije upisuju? |
| LQ-11 | P1 | Mogu li strani faktori bez saglasnosti u Srbiji kupovati domaća potraživanja preko platforme? |
| LQ-65 | P1 | Da li banke i društva za faktoring korišćenje platforme smatraju eksternalizacijom (npr. odluka NBS o upravljanju rizikom eksternalizacije za banke)? Ako da, koje ugovorne odredbe platforma mora da prihvati (pravo revizije, lokacija podataka, plan izlaska, podizvođači kao što je Azure)? |
| LQ-79 | P1 | Sme li platforma da kao dokaz ovlašćenosti faktora čuva snimak (PDF ili sliku) javnog registra APR ili spiska NBS i rešenje o saglasnosti, i koliko dugo? Da li platforma odgovara ako se faktoru saglasnost ukine u periodu između dve provere (sada 90 dana za društva za faktoring i 30 dana za banke, kao poslovni parametri)? Koji period ponovne provere je pravno dovoljan? (vidi LQ-09 i LQ-65) |

### 3.7 SEF i uvoz faktura
| ID | P | Pitanje |
|----|---|---------|
| LQ-21 | P0 | Da li je po pravilima SEF-a dozvoljeno da prodavac da svoj SEF API ključ platformi radi čitanja faktura? Da li je potrebno formalno ovlašćenje? *Radna pretpostavka (product owner, 2026-10-22):* prodavac sme da da svoj SEF API ključ platformi radi čitanja faktura, a dovoljno ovlašćenje je saglasnost u pristupnici (ADR-020); razvoj nastavlja na toj osnovi. Pretpostavka ne pokriva odgovornost i obaveze zaštite zbog punog opsega ključa (LQ-51). Pitanje ostaje otvoreno do pisanog odgovora advokata. |
| LQ-51 | P0 | SEF API ključ nema opseg samo za čitanje (provereno u demo Swagger-u, 2026-10-13): ključ koji dobijamo omogućava i storniranje, otkazivanje i brisanje faktura. Kakva odgovornost i obaveze zaštite iz toga slede za platformu? Šta moraju sadržati ovlašćenje prodavca i naši uslovi? |
| LQ-22 | P1 | Ima li SEF (ili planira) oznaku za faktoring ili ustupanje? Potvrdite pravilo da se faktura koja je predmet faktoringa ne može stornirati ili otkazati, i navedite član. |
| LQ-23 | P0 | Da li je faktura u SEF-u sa statusom „prihvaćena” od kupca pravno priznanje duga od strane dužnika? Ovo koristimo kao uslov prihvatljivosti. |
| LQ-16 | P1 | Dužnici iz javnog sektora: da li su prihvatljivi i kako se to odnosi na Centralni registar faktura (CRF)? |

### 3.8 Potraživanja i izjave prodavca
| ID | P | Pitanje |
|----|---|---------|
| LQ-10 | P0 | Tačna definicija „kratkoročnog” potraživanja (najduži rok do dospeća). Da li je potraživanje kome je rok dospeća prošao ikada prihvatljivo? |
| LQ-12 | P1 | Delimično ustupanje: mogu li dva faktora otkupiti različite delove iste fakture? Kako se primenjuje zabrana iz čl. 22 Zakona o faktoringu? |
| LQ-40 | P0 | Koje garancije prodavca uneti u uslove platforme, a koje u ugovor o faktoringu (postojanje potraživanja, da nije ranije ustupljeno, da nema prebijanja, da ustupanje nije zabranjeno)? |
| LQ-50 | P1 | Fakture indeksirane u EUR, a plative u RSD: kako se pravno izražavaju ustupljeni iznos i kupoprodajna cena? |

### 3.9 Tržište i ponude
| ID | P | Pitanje |
|----|---|---------|
| LQ-36 | P0 | Smeju li identitet dužnika i podaci o fakturi biti prikazani većem broju faktora (poslovna tajna, klauzule o poverljivosti u ugovoru prodavca i dužnika)? Da li je dovoljna saglasnost prodavca? Da li su ti podaci poslovna tajna po Zakonu o zaštiti poslovne tajne, i koje mere (obaveza poverljivosti u Uslovima korišćenja za faktore, vodeni žig, evidencija pristupa) platforma treba da ima? |
| LQ-60 | P0 | Pravna priroda ponude faktora prema Zakonu o obligacionim odnosima: ponuda, poziv na ponudu ili uslovna ponuda? Faktor može odustati posle prihvatanja ako njegov CDD ne prođe. Da li je punovažna „obavezujuća ponuda pod uslovom CDD provere”? Odgovara li faktor ako odustane? Kako pravno funkcionišu rok važenja i povlačenje ponude pre prihvatanja? (vidi LQ-25) |
| LQ-04 | P1 | Sme li platforma prodavcu prikazivati poređenje ponuda, isticati „najbolju ponudu” ili indikativni godišnji trošak, a da se ne smatra finansijskim savetnikom ili kreditnim posrednikom? Platforma naplaćuje faktoru procenat diskonta, pa zarađuje više kada je cena za prodavca lošija. Da li to utiče na odgovor? Mora li platforma prodavcu, u Uslovima korišćenja ili pri poređenju ponuda, obelodaniti da faktor plaća naknadu i kako se ona računa? |
| LQ-58 | P1 | Postoje li zakonska ograničenja diskonta koji faktor može da naplati (zelenaški ugovor, ZOO čl. 141, ili posebno ograničenje za faktoring)? Može li platforma odgovarati za prikaz ili omogućavanje ponude koju sud kasnije oceni kao zelenašku? |
| LQ-55 | P1 | Zaštita konkurencije: platforma prikuplja ponude konkurentskih faktora. Postoji li rizik razmene informacija ako faktori vide zbirne podatke (procenat uspeha, raspon cena)? Da li su ekskluzivitet ili period bez naknade za prve partnere, ili dominantan položaj u ovoj niši, problem? |

### 3.10 Posao: ugovor, potpis, ustupanje, regres
| ID | P | Pitanje |
|----|---|---------|
| LQ-25 | P0 | Može li prihvatanje ponude na platformi biti obavezujući predugovor, ili obavezuje samo potpisan ugovor o faktoringu? Kada potraživanje prelazi na faktora: potpisom, isplatom ili obaveštenjem dužnika? |
| LQ-24 | P0 | Koji nivo potpisa je potreban za ugovor o faktoringu i za izjave prodavca: kvalifikovani elektronski potpis, napredni, ili prihvatanje klikom? |
| LQ-27 | P1 | Sme li platforma da ponudi standardni obrazac ugovora o faktoringu, ili svaki faktor mora koristiti svoj? Ko odgovara za sadržaj obrasca? |
| LQ-59 | P1 | Okvirni ugovor: mogu li faktor i prodavac jednom potpisati okvirni ugovor (QES), a pojedinačne fakture ustupati specifikacijom potvrđenom na platformi (model Finspot/FINA u [market-analysis.md](market-analysis.md))? Šta mora sadržati specifikacija i koji nivo potpisa joj treba (vidi LQ-24)? |
| LQ-13 | P0 | Faktoring sa regresom i bez regresa: obavezan sadržaj ugovora za svaki. Ima li platforma ikakvu obavezu u vezi sa regresom (čl. 16, obaveštenje u roku od 8 dana)? |
| LQ-63 | P1 | Da li su obaveštenja poslata preko platforme (u aplikaciji ili na registrovani email) pravno valjana između strana i od platforme, npr. prihvatanje, otkaz, raskid, obaveštenje o regresu? Mogu li Uslovi korišćenja te kanale odrediti kao ugovoreni način dostavljanja? |

### 3.11 Centralna evidencija faktoringa (CEF) i obaveštenje dužnika
| ID | P | Pitanje |
|----|---|---------|
| LQ-14 | P0 | Obaveštenje dužnika: ko obaveštava, u kojoj formi i kada? Da li od početka rada CEF-a upis u CEF ispunjava ili zamenjuje obaveštenje? Sme li platforma slati obaveštenje u ime prodavca ili faktora? |
| LQ-17 | P0 | Tačan okidač obaveze ustupioca da evidentira fakturu u CEF („odmah po evidentiranju u SEF-u, najkasnije narednog radnog dana”): šta to znači za fakture ustupljene nedeljama posle izdavanja? |
| LQ-18 | P1 | Sme li platforma evidentirati fakture u CEF u ime korisnika (API ili ovlašćeni posrednik), s obzirom na zahtev za eID visokog nivoa? |
| LQ-19 | P1 | Pravno dejstvo upisa u CEF: prvenstvo između više prijemnika, oslobađanje dužnika od obaveze, odnos prema ništavim kasnijim ugovorima. |
| LQ-20 | P0 | Dok CEF ne radi: šta platforma mora da saopšti faktorima o preostalom riziku dvostrukog finansiranja van platforme? Ovo je važno i za eventualni zatvoreni pilot pre CEF-a (ADR-019). |

### 3.12 Prevare, sporovi i odgovornost
| ID | P | Pitanje |
|----|---|---------|
| LQ-39 | P0 | Kolika je odgovornost platforme ako se verifikovana faktura pokaže lažnom, ili ako faktoru bude oduzeta saglasnost usred posla? Isto pitanje za tehničke greške: prekid rada, nedostupnost SEF-a koja blokira korak, propušten rok za potpis ili isplatu, pogrešan rezultat provere. Da li je ograničenje odgovornosti moguće i punovažno? |
| LQ-57 | P1 | Da li je platforma pružalac usluga informacionog društva po Zakonu o elektronskoj trgovini? Koje informacije mora objaviti na sajtu i u Uslovima korišćenja? Može li se pozvati na izuzeće od odgovornosti hosting provajdera za dokumente, izjave i ponude koje unose korisnici? |
| LQ-41 | P1 | Rešavanje sporova: uloga platforme (nikakva, olakšavanje ili medijacija), merodavno pravo, nadležnost suda ili arbitraža. |
| LQ-42 | P1 | Obaveze platforme kada sumnja na prevaru ili dvostruko finansiranje: zamrzavanje oglasa, obaveštavanje faktora, prijava. |
| LQ-68 | P2 | Da li je platformi potrebno osiguranje od profesionalne odgovornosti ili sajber osiguranje, po zakonu ili po očekivanju banaka i faktora? Kakvo pokriće je uobičajeno za ovakvog posrednika? |

### 3.13 Podaci o ličnosti, bezbednost i čuvanje
| ID | P | Pitanje |
|----|---|---------|
| LQ-34 | P0 | Uloge rukovaoca i obrađivača: platforma naspram faktora, za podatke o zastupnicima prodavaca i kontakt osobama dužnika. |
| LQ-35 | P0 | Prenos podataka u Azure EU region po ZZPL (primerena zaštita, potreba za standardnim ugovornim klauzulama). Postoji li obaveza čuvanja finansijskih podataka u Srbiji? |
| LQ-37 | P1 | Da li su potrebni lice za zaštitu podataka (DPO), procena uticaja (DPIA) i evidencija radnji obrade? |
| LQ-38 | P1 | Beleženje IP adrese i podataka o uređaju za audit i sprečavanje prevara: pravni osnov i rok čuvanja. |
| LQ-64 | P1 | Bezbednosni incidenti: obaveštavanje o povredi podataka o ličnosti (Poverenik i lica, rokovi). Da li je platforma operator IKT sistema od posebnog značaja po Zakonu o informacionoj bezbednosti (npr. kao deo infrastrukture finansijskog tržišta), sa obavezom akta o bezbednosti i prijave incidenata? |
| LQ-66 | P1 | Kolačići i analitika u web aplikaciji (Zakon o elektronskim komunikacijama, ZZPL): šta zahteva pristanak i u kom obliku? Smeju li se slati obaveštenja o proizvodu i marketinške poruke na lične poslovne adrese korisnika bez prethodnog pristanka? Činjenica (task 019): faktori mogu da uključe satni pregled novih oglasa po svojim filterima; to je opcioni tip obaveštenja koji korisnik može da isključi. |
| LQ-67 | P2 | Sme li platforma koristiti zbirne ili anonimizovane podatke o transakcijama za izveštaje, uporedne podatke za faktore (npr. prosečan diskont po roku) ili javne izveštaje o tržištu? Šta moraju da sadrže Uslovi korišćenja, i kako se to odnosi na poslovnu tajnu (LQ-36) i zaštitu konkurencije (LQ-55)? |
| LQ-47 | P0 | Rokovi čuvanja po vrsti zapisa: poslovi i ugovori, KYB dokumenti, audit zapisi, fakture, fakture za naknade (Zakon o računovodstvu, Zakon o elektronskom fakturisanju, AML). Dopuna (task 019): uključiti i zapise o obaveštenjima (adresa primaoca, tip događaja, referenca posla, status slanja) i njihovo brisanje pri brisanju naloga. |
| LQ-48 | P1 | Pravo na brisanje naspram obaveznog čuvanja: kako postupati sa zahtevima za brisanje naloga? |
| LQ-80 | P1 | Platforma čuva izvod iz registra računa NBS i, po potrebi, potvrdu banke kao dokaz da je račun za isplatu upisan na PIB i matični broj firme (dokument sadrži broj računa, naziv i matični broj firme, ponekad i ime zastupnika). Koliko dugo se ti dokumenti čuvaju (dok račun traje, posle deaktivacije, u KYB dosijeu), koji je pravni osnov obrade i važi li isti rok za zapis o deaktiviranom računu u revizorskom tragu? (vidi LQ-47) |
| LQ-81 | P1 | Platforma faktoru prikazuje pun broj računa prodavca za isplatu tek u koraku finansiranja posla (pristup se evidentira), a ostalima samo maskiran broj. Koji je pravni osnov tog otkrivanja (izvršenje ugovora ili saglasnost iz Uslova), da li prodavca treba o tome obavestiti u Uslovima korišćenja i ima li platforma dodatne obaveze zaštite tih podataka? |
| LQ-82 | P1 | Kada isti broj računa prijave dve različite firme, platforma automatski blokira proveru tih zapisa i pravi interno upozorenje za službenika usklađenosti, bez obaveštavanja firmi o razlogu. Sme li platforma tako da postupi, mora li sumnju da prijavi nadležnom organu ili faktoru koji ima posao u toku i šta sme da saopšti firmama? (vidi LQ-42 i LQ-78) |
| LQ-70 | P1 | Da li potpisane ugovore, izjave i audit dokaze moramo čuvati kod pružaoca usluge kvalifikovanog elektronskog čuvanja (zakon o elektronskom dokumentu) da bi zadržali dokaznu snagu? Ili je dovoljno naše čuvanje (heš vrednosti, WORM zapisi)? Da li se primenjuju i propisi o arhivskoj građi? |

### 3.14 Marketing i brend
| ID | P | Pitanje |
|----|---|---------|
| LQ-56 | P1 | Oglašavanje platforme i ponuda faktoringa (Zakon o oglašavanju i pravila za finansijske usluge): koje tvrdnje su ograničene („novac za 24 sata”, „najbolja cena”, poređenje sa bankama)? Mora li platforma isticati da nije faktor ni kreditor? |
| LQ-71 | P2 | Može li se registrovati naziv „Cesio” kao žig (Zavod za intelektualnu svojinu, po potrebi i EU)? Postoje li sukobi sa postojećim žigovima u finansijskim uslugama? Šta je sa domenima? |

### 3.15 Kasnije faze (P1/P2, za planiranje)
| ID | P | Pitanje |
|----|---|---------|
| LQ-05 | P2 | Da li bi sopstveni otkup potraživanja (bilansno učešće platforme) zahtevao posebnu saglasnost za faktoring i razdvajanje radi sukoba interesa? |
| LQ-15 | P1 | Obrnuti faktoring (na inicijativu dužnika): postoje li dodatni zahtevi? |
| LQ-49 | P2 | Prekogranično poslovanje: strani prodavci, strani dužnici (izvozne fakture van SEF-a), potraživanja u EUR, devizni propisi. |

## 4. Najvažnija pitanja (prvi krug)
Ako advokat radi u fazama, prvi krug čine ključna pitanja iz [kljucna-pitanja-za-advokata.txt](kljucna-pitanja-za-advokata.txt). Od njih zavisi da li je model uopšte izvodljiv:
1. LQ-01, LQ-02: status platforme;
2. LQ-06: tok novca;
3. LQ-03: naknada;
4. LQ-21, LQ-51: SEF ključ;
5. LQ-36: prikaz dužnika većem broju faktora;
6. LQ-28: AML status;
7. LQ-35: podaci u Azure-u u EU;
8. LQ-72: registracija i Uslovi korišćenja umesto ugovora sa faktorom.

Odmah zatim slede ostala pitanja P0, pre svega: LQ-53 (mišljenje regulatora), LQ-25, LQ-60 i LQ-61 (obavezujuća snaga ponude i prihvatanja, ovlašćenja potpisnika), LQ-29 (deljenje KYB paketa) i LQ-73 (ko prihvata Uslove za faktora). PDV na naknadu (LQ-43) ide poreskom savetniku u istom krugu.

## 5. Održavanje ovog dokumenta
- **Jezik:** pitanja i odgovori pišu se isključivo na srpskom (latinica).
- **Novo pitanje:**
  1. Proverite da li postojeće pitanje već pokriva temu; ako pokriva, dopunite ga.
  2. Upišite ga u odgovarajući deo §3.
  3. ID je najveći postojeći LQ broj + 1. ID-jevi se nikad ne menjaju, ne brišu i ne koriste ponovo.
  4. Poreska pitanja nose oznaku *(porez)*.
  5. Ako pitanje blokira neki task, dodajte ga u „Blocked by” tog taska.
- **Istraživanje** koje ne zatvara pitanje upisuje se uz njega: *Istraživanje GGGG-MM-DD:* nalaz (izvor u regulatory.md Sx). Ostaje pitanje: …
- **Odgovor** se upisuje ispod pitanja u formatu iz §1, a zatim se ažuriraju regulatory.md, decisions.md i taskovi koji su upućivali na pitanje.
- Pitanje se zatvara **samo** pisanim odgovorom advokata ili poreskog savetnika, nikad na osnovu istraživanja na internetu ili AI sažetka.
- **Radna pretpostavka** product ownera upisuje se uz pitanje: *Radna pretpostavka (product owner, GGGG-MM-DD):* pretpostavljeni odgovor. Razvoj i taskovi mogu da nastave na toj osnovi (pitanje se uklanja iz „Blocked by”), ali pitanje ostaje otvoreno i ide advokatu. Ako odgovor bude drugačiji, posledice se procenjuju kroz novi ADR.
- Agent `legal-review` (`.claude/agents/legal-review.md`) proverava izmene sa pravnim posledicama i predlaže nova pitanja.
- **[pitanja-za-advokata.md](pitanja-za-advokata.md)** je verzija za slanje advokatu: samo pravna pitanja (bez *(porez)*), numerisana jedno po jedno, sa objašnjenjem šta želimo da napravimo. Kada se ovde doda ili izmeni pravno pitanje, u istoj izmeni se ažurira i taj fajl. Izvor istine ostaje ovaj registar.
- **[kljucna-pitanja-za-advokata.txt](kljucna-pitanja-za-advokata.txt)** je tekstualni fajl sa opisom modela i samo 9 ključnih pitanja (LQ-01, 02, 03, 06, 21/51, 28, 35, 36, 72). Kada se neko od tih pitanja izmeni, ažurira se i taj fajl.
