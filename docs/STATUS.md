# Stav projektu

**Posledná aktualizácia:** 2026-11-06
**Fáza:** vývoj, testovanie ďalších verzií TIA
**Verzia add-inu:** 0.2.6 (`Product/Version` v `AddInPublisherConfig.*.xml`)
**Verzia kódu:** commit `424aee1` na `feat/multi-version-tia`

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

Ostatné verzie sú v [07-testovanie-verzii.md](07-testovanie-verzii.md).

## Čo neprešlo testom

- TIA V16, V18, V19, V20.
- Multiuser projekty a Local Sessions.
- `CorporateAddIns`, podpísaný add-in, nasadenie cez IT.
- Bežný používateľ bez administrátorských práv.
- Export z neotvoreného projektu a archívu cez CLI po posledných zmenách.
- Validácia výstupu podľa schémy CycloneDX.
- Obnova Gitea zo zálohy. Pravidelná záloha zatiaľ neexistuje.

## Známe problémy

Úplný zoznam je v [06-kod.md](06-kod.md#známe-problémy-a-vylepšenia). Najdôležitejšie:

1. Opakovaný export tej istej verzie prepíše `versions/<názov>/sbom.json`, ale tag zostane na prvom commite. Tag a súbor sa tak rozídu.
2. `IV3-XXXX` a podobné záznamy z GSDML nie sú skutočný model a ich „firmvér“ je verzia popisu, nie zariadenia.
3. Chýba mapovanie objednávacích čísel na CPE, tracker ho bude potrebovať.
4. SBOM obsahuje hardvér a použité inžinierske produkty, nie knižnice ani bloky PLC.

## Stav vetiev

| Vetva | Stav |
|---|---|
| lokálny `main` | kód z `424aee1` a dokumentácia, pred GitHub `main` |
| `feat/multi-version-tia` | na `424aee1`, pushnutá |
| GitHub `main` | na `60f5841`, push čaká na súhlas používateľa |

## Prostredie

| Časť | Stav |
|---|---|
| Gitea | beží vo WSL ako vývojová inštancia, nie ostrý server |
| Zálohy Gitea | žiadne |
| Pripojenie VM | HTTP cez `netsh portproxy`, len na Hyper-V prepínači |

## Ďalšie kroky

1. Otestovať V18, V19, V20 a V16 podľa [07-testovanie-verzii.md](07-testovanie-verzii.md).
2. Rozhodnúť správanie pri opakovanom exporte tej istej verzie.
3. Pripraviť produkčný server a zálohy podľa [04-produkcia.md](04-produkcia.md).

## História míľnikov

| Dátum | Míľnik |
|---|---|
| 2026-10-29 | Prvý export z V21 do Gitea, add-in, automatické repozitáre a ochrana pred zámenou |
| 2026-11-05 | Add-in funguje na V17 v sandboxe a na V21, panely HMI v SBOM |
| 2026-11-06 | Dokumentácia a pravidlá projektu |
