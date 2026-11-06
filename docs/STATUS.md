# Stav projektu

**Posledná aktualizácia:** 2026-11-06
**Fáza:** vývoj, testovanie ďalších verzií TIA
**Verzia add-inu:** 0.2.10 lokálne, overená 0.2.7 na V19 a 0.2.6 na V17 a V21 (`Product/Version` v `AddInPublisherConfig.*.xml`)
**Verzia kódu:** `main` má overenú 0.2.6, `feat/multi-version-tia` testovanú 0.2.7

> Tento súbor sa aktualizuje na konci každého chatu, v ktorom sa niečo zmenilo.

## Čo je overené

| Oblasť | TIA V17 | TIA V21 |
|---|---|---|
| Build a publisher | áno | áno |
| CLI export do súboru | áno | áno, staršia verzia kódu |
| CLI `--dump-hw` | áno | nie |
| Add-in: export do Gitea | áno, 0.2.6 | áno, 0.2.6 |
| Dialóg výberu kľúča pri prázdnom Family | áno | áno |
| Panel HMI v SBOM | áno | áno |
| Čítanie uloženého tokenu | áno | áno |

TIA V19 je overená add-inom 0.2.7 vrátane exportu do Gitea. Na V17 a V21 je overená 0.2.6, verziu 0.2.7 treba zopakovať. Ostatné verzie sú v [07-testovanie-verzii.md](07-testovanie-verzii.md).

## Čo neprešlo testom

- TIA V16, V18, V20.
- Multiuser projekty a Local Sessions.
- `CorporateAddIns`, podpísaný add-in, nasadenie cez IT.
- Bežný používateľ bez administrátorských práv.
- Export z neotvoreného projektu a archívu cez CLI po posledných zmenách.
- Validácia výstupu podľa schémy CycloneDX.
- Obnova Gitea zo zálohy. Pravidelná záloha zatiaľ neexistuje.

## Známe problémy

Úplný zoznam je v [06-kod.md](06-kod.md#známe-problémy-a-vylepšenia). Najdôležitejšie:

1. Verzia 0.2.10-rc1 je na V17, V19 a V21 overená v hlavných scenároch. Na V21 sa raz stalo, že okno s hláškou ostalo nevidieť, nezopakovalo sa to. `IsModified` nie je v neúplnej dokumentácii V20 potvrdené.
2. `IV3-XXXX` a podobné záznamy z GSDML nie sú skutočný model a ich „firmvér“ je verzia popisu, nie zariadenia.
3. Chýba mapovanie objednávacích čísel na CPE, tracker ho bude potrebovať.
4. SBOM obsahuje hardvér a použité inžinierske produkty, nie knižnice ani bloky PLC.

## Stav vetiev

| Vetva | Stav |
|---|---|
| GitHub `main` | overená 0.2.6 a dokumentácia |
| `feat/multi-version-tia` | 0.2.7, podpora add-inu V19 a oprava GSD, testuje sa z nej |
| lokálny `main` | rovnaký obsah ako `feat/multi-version-tia`, GitHub `main` čaká na overenie |

## Prostredie

| Časť | Stav |
|---|---|
| Gitea | beží vo WSL ako vývojová inštancia, nie ostrý server |
| Zálohy Gitea | žiadne |
| Pripojenie VM | HTTP cez `netsh portproxy`, len na Hyper-V prepínači |

## Ďalšie kroky

1. Otestovať V18 a V20 podľa [07-testovanie-verzii.md](07-testovanie-verzii.md).
2. Dokončiť testy výnimiek v [07-testovanie-verzii.md](07-testovanie-verzii.md#testy-výnimiek).
3. Overiť inštalátor a aktiváciu add-inu na skutočnom TIA podľa [09-instalacia.md](09-instalacia.md).
4. Pripraviť produkčný server a zálohy podľa [04-produkcia.md](04-produkcia.md).

## História míľnikov

| Dátum | Míľnik |
|---|---|
| 2026-10-29 | Prvý export z V21 do Gitea, add-in, automatické repozitáre a ochrana pred zámenou |
| 2026-11-05 | Add-in funguje na V17 v sandboxe a na V21, panely HMI v SBOM |
| 2026-11-06 | Dokumentácia a pravidlá projektu, add-in overený na V19 |
| 2026-11-06 | Atomický zápis a súbežné verzie overené na V19, 0.2.10-rc1 |
