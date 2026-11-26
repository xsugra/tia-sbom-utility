# Stav projektu

**Posledná aktualizácia:** 2026-11-26
**Fáza:** vývoj, príprava nasadenia na 10 až 20 PC
**Verzia add-inu:** 0.2.12 overená na V21 proti produkčnej Gitea (inštalácia, CA, `check.cmd`, bez exportu), na V17 až V20 neoverená. 0.2.11 je overená na V17, V18, V19, V20 a V21 (`Product/Version` v `AddInPublisherConfig.*.xml`)
**Verzia kódu:** `main` má 0.2.12, tag `v0.2.12`, na GitHube od 2026-11-26

> Tento súbor sa aktualizuje na konci každého chatu, v ktorom sa niečo zmenilo.

## Čo je overené

| Oblasť | TIA V17 | TIA V19 | TIA V21 |
|---|---|---|---|
| Build a publisher | áno, 0.2.11 | áno, 0.2.11 | áno, 0.2.11 |
| Add-in: export do Gitea | áno, 0.2.11 (už uložený obsah) | áno, 0.2.11 (už uložený obsah) | áno, 0.2.11 |
| Inštalátor | áno, 0.2.11, `Portal V17\AddIns` | áno, 0.2.11, `Portal V19\AddIns` | áno, 0.2.11, `CorporateAddIns` |
| Aktivácia a aktualizácia add-inu | – | – | áno: aktualizácia add-in deaktivuje |
| Chybové hlášky `TS-…` v TIA | `TS-NET-04` | `TS-NET-04` | `TS-NET-01/02/04`, `TS-AUTH-01` |
| Okná nad TIA | áno, 0.2.11 v sandboxe, Ctrl+C funguje | áno, 0.2.11 v sandboxe, Ctrl+C funguje | áno, nové okno hlásení 0.2.11 |
| Dialóg pri prázdnom Family | áno | áno | áno |
| `check.cmd` s tokenom používateľa | áno | áno | áno |

V18 a V20: 0.2.11 overená 2026-11-19 rovnakým postupom ako V19, podrobnosti v [07-testovanie-verzii.md](07-testovanie-verzii.md).

Automatické testy (overené 2026-11-19, podrobnosti v [12-automaticke-testy.md](12-automaticke-testy.md)):

| Sada | Výsledok |
|---|---|
| WSL net8.0 s vývojovou Gitea 28.2.0 | 86 z 86 (2026-11-19, jeden beh 85 z 86, súbežný export je občas nestabilný) |
| VM net48 bez Gitea | unit testy a hlášky OK |
| Schéma CycloneDX 1.6 | výstup je platný |

## Opravené 2026-11-18

Nájdené testami. Bez opravy by sa ukázali až u používateľov:

1. **Okná add-inu na V21 za TIA.** Add-in beží vo vlastnom procese, `MessageBox` sa otvoril za TIA a nemal ikonu na paneli úloh. Používateľ nevidel nič. Teraz vlastné okno `TopMost` s ikonou.
2. **Súbežný export** hlásil „nemáte právo zapisovať“. Gitea 28 vracia pri súbehu 403 `cannot lock ref` alebo 500. Teraz až 5 pokusov.
3. Chybové hlášky neodlišovali DNS, vypnutý server, timeout, certifikát, HTML namiesto API ani chýbajúce oprávnenia tokenu. Teraz kódy `TS-…` s krokom pre používateľa a cestou k logu, katalóg je v [11-chybove-hlasky.md](11-chybove-hlasky.md).
4. Neplatná `BaseUrl`, `TimeoutSeconds=0` alebo poškodený `config.json` padali bez zrozumiteľnej hlášky.
5. Inštalátor pre V21 dával add-in iba do profilu správcu. Teraz `ADDIN_SCOPE=corporate`.

## 0.2.12, 2026-11-19, overené na V21 proti produkčnej Gitea

- Kontrola spojenia (`check.cmd`, inštalátor) berie 403 z produkčnej Gitea s `REQUIRE_SIGNIN_VIEW=true` ako dostupný server. Predtým by hlásila chybu. Overené na V21.
- `GITEA_CA_CERT` v `tiasbom.env`: inštalátor pridá koreňovú CA Caddy do dôveryhodných na PC, kým nie je certifikát firemnej CA. Overené na V21, inštalátor kód 0, `check.cmd` `RESULT: OK` s osobným tokenom.
- Export z TIA do produkčnej Gitea zatiaľ nebol, ani 0.2.12 na V17 až V20.
- Add-in sa nemení správaním, verzia je zvýšená kvôli zmene `TiaSbom.Gitea`.

## Nové

- `tiasbom --check` a `check.cmd`: kontrola spojenia, tokenu a oprávnení bez TIA.
- Inštalátor otestuje spojenie a pri chybe vráti kód 3.
- Testovací projekt `tests/TiaSbom.Tests`, `test.sh`, `test.ps1`.
- `tools/vm-remote`: ovládanie VM s TIA z laptopu cez SSH, OCR a screenshoty.
- `tools/redact-screenshot.ps1`, `tools/dev/fix-gitea-portproxy.cmd`.
- Príručka pre kolegov ako Word a PDF v `docs/export/` (kapitoly 10, 11 a 09), generátor `tools/docs-export`. Markdown v `docs/` ostáva zdrojom. Skript `export-docs.ps1` zatiaľ visí vo Worde pri `SaveAs2`, príčina nezistená.
- Dokumentácia pre kolegov: [10-pouzivatelska-prirucka.md](10-pouzivatelska-prirucka.md) so screenshotmi, [11-chybove-hlasky.md](11-chybove-hlasky.md), [12-automaticke-testy.md](12-automaticke-testy.md).

## Čo neprešlo testom

- Nový zápis verzie s 0.2.11 na V17 až V20 (testovaný bol iba už uložený obsah, zápis overený na V21).
- TIA V16.
- Multiuser projekty a Local Sessions.
- GPO, účet SYSTEM, Siemens Add-In Rollout Service.
- Bežný používateľ bez administrátorských práv.
- V TIA: neuložený projekt, archivovaný repozitár, vyplnené `Family`, varovanie o zámene.
- Obnova Gitea zo zálohy. Pravidelná záloha zatiaľ neexistuje.

## Známe problémy

Úplný zoznam je v [06-kod.md](06-kod.md#známe-problémy-a-vylepšenia). Najdôležitejšie:

1. **Každý používateľ aktivuje add-in sám, aj po každej aktualizácii.** Overené na V21 pri nepodpísanom balíku.
2. Adresa vývojovej Gitea sa mení po reštarte laptopu. Produkcia potrebuje DNS meno alebo pevnú IP a HTTPS s dôveryhodnou CA, pozri [04-produkcia.md](04-produkcia.md#adresa-servera).
3. `IV3-XXXX` a podobné záznamy z GSDML nie sú skutočný model a ich „firmvér“ je verzia popisu, nie zariadenia.
4. Chýba mapovanie objednávacích čísel na CPE.
5. Autor add-inu v TIA je zástupný text `YourCompany – Automation Tools`.

## Stav vetiev

| Vetva | Stav |
|---|---|
| `main` (lokálny aj GitHub) | 0.2.12, tag `v0.2.12`, pushnutý 2026-11-26. História na GitHube má od 2026-11 e-mail `noreply`, nové commity s ním |
| `claude/tia-sbom-automation-testing-d1becd` | zlúčená do `main` |

## Prostredie

| Časť | Stav |
|---|---|
| Gitea | beží vo WSL ako vývojová inštancia, testovací účet `sbom-test` a organizácia `sbom-test-org` |
| Zálohy Gitea | žiadne |
| Pripojenie VM | HTTP cez `netsh portproxy`, len na Hyper-V prepínači |
| Produkčný server | Beží na Windows serveri vo WSL, `https://<IP>` dostupné z firemnej siete (overené 2026-11-12 z iného PC). Autoštart cez úlohu S4U overený po reštarte. Certifikát zatiaľ `tls internal`, PC mu dôverujú cez `GITEA_CA_CERT`. `REQUIRE_SIGNIN_VIEW=true`. Konfigurácia a postup v `xsugra/gitea-server`. DNS a certifikát firemnej CA chýbajú. Inštalácia a `check.cmd` z VM V21 overené 2026-11-19, export add-inu cez HTTPS ešte nie |
| VM V21 | SSH iba kľúčom, iba z Default Switch, na automatizované testy |

## Ďalšie kroky

1. Prvý export z TIA V21 do produkčnej Gitea s 0.2.12 na testovacom projekte. Vydanie 0.2.12 je v `main` bez neho, so súhlasom používateľa.
2. Zálohy produkčnej Gitea a test obnovy. Stála adresa a certifikát firemnej CA podľa [04-produkcia.md](04-produkcia.md#adresa-servera).
3. Rozhodnúť o servisnom tokene verzus osobných tokenoch. Add-in sa nepodpisuje, rozhodnutie 2026-11-19.
4. Zmeniť autora add-inu `YourCompany` na názov firmy.
5. Pilot na 2 až 3 PC podľa [09-instalacia.md](09-instalacia.md#nasadenie-na-10-až-20-počítačov).

## História míľnikov

| Dátum | Míľnik |
|---|---|
| 2026-10-29 | Prvý export z V21 do Gitea, add-in, automatické repozitáre a ochrana pred zámenou |
| 2026-11-05 | Add-in funguje na V17 v sandboxe a na V21, panely HMI v SBOM |
| 2026-11-06 | Dokumentácia a pravidlá projektu, add-in overený na V19 |
| 2026-11-06 | Atomický zápis a súbežné verzie overené na V19, 0.2.10-rc1 |
| 2026-11-18 | Automatické testy, chybové kódy, inštalácia do CorporateAddIns, end-to-end test na V21 cez SSH, 0.2.11 |
| 2026-11-19 | 0.2.12: produkčná Gitea cez HTTPS s `GITEA_CA_CERT`, inštalácia a `check.cmd` z V21 overené |
