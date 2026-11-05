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

.NET Framework podľa verzie Openness. **Žiadne NuGet závislosti** – add-in beží v procese TIA
bez binding redirectov, cudzie knižnice (System.Text.Json, CycloneDX.Core) sa tam nenačítajú.

## Podporované verzie TIA Portal

Jeden zdrojový kód, jeden build na každé Openness API:

| Build (`-Api`) | Beží na TIA | .NET Framework |
|---|---|---|
| `V16` | V16 – V19 | 4.6.2 |
| `V17` | V17 – V20 | 4.8 |
| `V21` | iba V21 | 4.8 |

```powershell
.\build.ps1 -Api V17
.\build.ps1 -Api V21 -AddIn
```

- `build.ps1` berie DLL z `Portal <Portal>\PublicAPI\<Api>`. Na stroji bez danej verzie TIA použite napr. `-Api V17 -Portal V19`.
- `tiasbom.exe` nájde správnu inštaláciu v registri podľa prípony projektu, bežiaceho TIA alebo `--portal Vxx`.
- SBOM uvádza skutočnú verziu TIA z prípony projektu (`tia:version`) a API buildu (`tia:opennessApi`).
- Starší projekt sa neotvára cez `OpenWithUpgrade`, lebo vytvára povýšenú kópiu. Projekt sa spracuje vo svojej verzii TIA.
- `Device.UnpluggedItems` je overené vo V17 aj V21. Build V16 ho zatiaľ nečíta, v SBOM má `tia:unpluggedItemsRead = false`.
- Add-in má konfiguráciu pre každé API: `AddInPublisherConfig.<Api>.xml`. Do V20 je jeho API a publisher v `PublicAPI\<Api>.AddIn`.
- V17 nemá `UnrestrictedPermissions`, add-in tam beží v sandboxe s explicitnými oprávneniami. V21 je overený, V17 sa testuje.

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
6. **Add-In**:
   ```
   powershell -ExecutionPolicy Bypass -File .\build.ps1 -Api V17 -AddIn
   ```
   Test: `%APPDATA%\Siemens\Automation\Portal V21\UserAddIns`.
   Firma: `C:\ProgramData\Siemens\Automation\Portal V21\CorporateAddIns` (IT), podpísať firemným certifikátom.
7. **Použitie** – TIA → záložka *Add-ins* → aktivovať → pravý klik na projekt → **SBOM → Export SBOM to Gitea**.
8. **Diagnostika add-inu** – každý krok exportu sa zapisuje do `%APPDATA%\TiaSbom\addin.log`.

## Otvorené body

- Oprávnenia add-inu sú `UnrestrictedAccess` (DPAPI nemá v schéme V21 vlastné oprávnenie) – sprísniť.
- Mapovanie objednávacie číslo → CPE (pre párovanie zraniteľností) – `SbomBuilder`.
- Multiuser (Local Session) projekty – otestovať zvlášť.
- SW časť: zatiaľ len `UsedProducts`; knižnice a verzie blokov nepokryté.
