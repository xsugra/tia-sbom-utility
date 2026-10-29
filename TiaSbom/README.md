# TiaSbom – SBOM z TIA Portal V21 do Gitea

Jedno kliknutie v TIA Portali → CycloneDX 1.6 SBOM (HW + firmvér + použitý TIA softvér) → commit do Gitea.

## Štruktúra

| Projekt | Úloha | Závisí od TIA |
|---|---|---|
| `TiaSbom.Core` | model, CycloneDX builder, vlastný JSON writer, projektový kľúč | nie |
| `TiaSbom.Tia` | čítanie HW a UsedProducts cez Openness V21 | áno |
| `TiaSbom.Gitea` | Gitea REST API (repo, súbory, tagy), DPAPI token | nie |
| `TiaSbom.Cli` | `tiasbom.exe` – ladenie, CI, hromadné spracovanie | áno |
| `TiaSbom.AddIn` | kontextové menu v TIA (pre bežných používateľov) | áno |

.NET Framework 4.8 (požiadavka Openness V21). **Žiadne NuGet závislosti** – add-in beží v procese TIA
bez binding redirectov, cudzie knižnice (System.Text.Json, CycloneDX.Core) sa tam nenačítajú.

## Projektový kľúč = Gitea repozitár

Názvy TIA projektov sa líšia a menia s každou verziou, preto je kľúčom vlastnosť projektu
**Project → Properties → Family** (kopíruje sa so „Save as“, archívom aj novou verziou).

- Family vyplnené → export ide do repozitára `<Owner>/<Family>`, ktorý sa vytvorí automaticky.
- Family prázdne → add-in ponúkne existujúce repozitáre alebo nový kľúč (návrh: číslo zákazky na začiatku názvu)
  a pripomenie vyplniť Family. Openness V21 vlastnosti projektu zapisovať nevie.
- CLI: `--key <kľúč>` alebo Family, inak chyba.

**Ochrana pred zámenou** (`MismatchCheck`) – pred uploadom sa porovná export s `latest.json` cieľového repozitára:
- menej ako 50 % rovnakých modulov (stanica + objednávacie číslo),
- číslo zákazky na začiatku názvu projektu ≠ numerický kľúč,
- Family v projekte ≠ cieľový kľúč.

Add-in vtedy zobrazí varovanie (predvolené *Nie*), CLI odmietne upload bez `--force`.

## Výstup v Gitea

```
<Owner>/                         organizácia, napr. "sbom" (všetci čítajú)
└── <Family>/                    1 repozitár = 1 projekt / zákazka
    ├── README.md                generovaný prehľad
    ├── latest.json              posledný SBOM (vstup pre tracker)
    └── versions/<názov projektu>/sbom.json   + git tag <názov projektu>
```

Po skončení zákazky repozitár v Gitea *Archive* → read-only, dohľadateľný (CRA 10 rokov).

## Postup

1. **Prostredie** – Visual Studio 2022 Professional (alebo VS Code + extension *C#*, bez C# Dev Kit),
   .NET SDK, .NET Framework 4.8 Developer Pack, TIA Portal V21.
   Používateľ musí byť v skupine **Siemens TIA Openness** (potom odhlásiť / prihlásiť).
2. **Gitea** – organizácia (napr. `sbom`), servisný účet s právom vytvárať repozitáre iba v nej, jeho token.
3. **Konfigurácia** – `deploy/config.example.json` → `%ProgramData%\TiaSbom\config.json` (IT cez GPO),
   `Token` = token servisného účtu. Bez neho sa použije osobný token (`tiasbom --login` / dialóg v add-ine).
4. **Build** – `dotnet build -c Release` (iná cesta k TIA: `-p:TiaApiDir=...`).
5. **Overenie cez CLI**:
   ```
   tiasbom --attach --out sbom.json
   tiasbom --attach --upload --key 24337
   ```
6. **Add-In** – v `src\TiaSbom.AddIn\bin\Release\net48`:
   ```
   "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\Siemens.Engineering.AddIn.Publisher.exe" -f AddInPublisherConfig.xml -o TiaSbom.addin -c
   ```
   Test: `%APPDATA%\Siemens\Automation\Portal V21\UserAddIns`.
   Firma: `C:\ProgramData\Siemens\Automation\Portal V21\CorporateAddIns` (IT), podpísať firemným certifikátom.
7. **Použitie** – TIA → záložka *Add-ins* → aktivovať → pravý klik na projekt → **SBOM → Export SBOM to Gitea**.

## Otvorené body

- Oprávnenia add-inu sú `UnrestrictedAccess` (DPAPI nemá v schéme V21 vlastné oprávnenie) – sprísniť.
- Mapovanie objednávacie číslo → CPE (pre párovanie zraniteľností) – `SbomBuilder`.
- Multiuser (Local Session) projekty – otestovať zvlášť.
- SW časť: zatiaľ len `UsedProducts`; knižnice a verzie blokov nepokryté.
