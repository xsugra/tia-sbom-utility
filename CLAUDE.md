# TIA SBOM Utility – pravidlá pre prácu na projekte

Tieto pravidlá platia pre každý Claude chat aj pre ľudí, ktorí na projekte pracujú.

## Začiatok každého chatu

1. Prečítaj [docs/STATUS.md](docs/STATUS.md): čo je overené, čo je otvorené a aký je ďalší krok.
2. Ak existuje `docs/local/environment.md`, prečítaj ho: obsahuje mená strojov a adresy, ktoré nesmú do Gitu.
3. Podľa úlohy otvor príslušný dokument z [docs/README.md](docs/README.md).
4. Spusti `git status -sb` a over vetvu. Používateľ ju mohol medzitým zmeniť.

## Komunikácia

- Odpovedaj po slovensky, krátko, ako senior SW engineer.
- Príkazy pre používateľa musia byť hotové na vloženie, bez zástupných textov typu `<priečinok>`. Ak hodnotu nepoznáš, daj príkaz, ktorý ju zistí.
- Vždy povedz, kde sa príkaz spúšťa: laptop, WSL, alebo konkrétna VM.
- Príkazy pre VM začínaj kontrolou, že je nasadená správna verzia. Pozri [docs/02-setup-vyvoj.md](docs/02-setup-vyvoj.md).
- Rozlišuj **overené na skutočnom TIA** od **odhadu**. „Hotovo“ znamená výsledok overený v TIA a v Gitea.

## Architektúra je daná

Riešenie sa nemení bez výslovného pokynu používateľa. Firma nesmie používať nástroje, ktorých bezplatná licencia sa na ňu nevzťahuje (pozri [licencie](docs/01-architektura.md#licencie)).

- SBOM: CycloneDX 1.6 JSON, vlastný zapisovač a parser.
- Úložisko: Gitea, jeden repozitár na projekt v organizácii `sbom`.
- Používateľ: add-in v TIA, jedno tlačidlo.
- Vývoj: VS Code + extension C# + .NET SDK. **Nie** Visual Studio Community ani C# Dev Kit.
- Docker Engine vo WSL, **nie** Docker Desktop.
- Tracker zraniteľností robí používateľ neskôr. Nerob ho bez zadania.

## Bezpečnosť (firemná sieť)

Podrobnosti sú v [docs/05-bezpecnost.md](docs/05-bezpecnost.md).

- Do Gitu ani do dokumentov nikdy nepatrí token, heslo, `config.json`, `token.bin`, `.env`, `tiasbom.env` ani certifikát.
- Do Gitu nepatria SBOM zo skutočných projektov, názvy zákazníkov, cesty k projektom ani mená strojov. Tie patria do `docs/local/`.
- Claude nikdy sám nemení firewall, `netsh portproxy`, publikovanie portov ani nastavenia zabezpečenia. Pripraví príkazy, vysvetlí ich a spustí ich používateľ.
- Port 3000 je len pre vývoj a nesmie byť dostupný z firemnej siete. Pred každým návrhom siete si polož otázku: „Vidí to iný počítač vo firme?“
- Pri čítaní `.env`, `app.ini` alebo konfigurácií maskuj tajné hodnoty ešte pred výpisom.
- Pred mazaním dát, volumes alebo repozitárov sa pozri na obsah a vyžiadaj súhlas.

## Git

- Commit a push iba na výslovnú požiadavku. Súhlas platí len pre konkrétnu akciu.
- `main` sa pushuje až po overení na TIA a so súhlasom používateľa. Pred pushom over vetvu.
- Stageuj konkrétne súbory a commituj s pathspec: `git commit -- <súbory>`. Index môže obsahovať zmeny používateľa.
- Správa commitu je anglicky a vysvetľuje prečo. Remote sa volá `tia-sbom-utility`, nie `origin`.
- Tagy: `vX.Y.Z` iba pre verziu overenú na TIA, `vX.Y.Z-rcN` pre neoverenú. Číslo zodpovedá `<Version>` add-inu. Tag je anotovaný, v správe uvádza overené verzie TIA a existujúci tag sa neprepisuje.

## Kód

- Celý kód musí fungovať v sandboxe V17 (čiastočná dôvera). V `TiaSbom.AddIn`, `TiaSbom.Core`, `TiaSbom.Gitea` a `HardwareCollector` je zakázané:
  - `System.Diagnostics.Process`,
  - `JavaScriptSerializer` a čokoľvek zo `System.Web.*`,
  - `Marshal`, `GCHandle`, `unsafe`,
  - akékoľvek NuGet balíky.
- Nové .NET API over sondou v sandboxe podľa [docs/06-kod.md](docs/06-kod.md#overenie-v-sandboxe).
- Na laptope nie je .NET SDK ani TIA. Prvý skutočný build prebehne vo VM, povedz to používateľovi.
- Pri zmene add-inu zvýš `<Version>` v **oboch** `AddInPublisherConfig.*.xml`.
- `#if` používaj len cez konštanty z `Directory.Build.props`, dokumentuj prečo.
- Nové vlastnosti TIA čítaj cez `Get<T>` tam, kde nemusia existovať na každej položke.
- Do logu nikdy nepíš token.

## Udržiavanie dokumentácie

Dokumentácia je súčasť zmeny. Úloha nie je hotová, kým nesedí.

| Čo sa zmenilo | Aktualizuj |
|---|---|
| Výsledok testu, stav, ďalší krok, vydanie add-inu | `docs/STATUS.md`, `docs/07-testovanie-verzii.md` |
| Architektúra, formát SBOM, rozhodnutie | `docs/01-architektura.md` |
| Build, VM, nasadenie add-inu | `docs/02-setup-vyvoj.md` |
| Gitea, WSL, Docker, sieť | `docs/03-gitea-server.md`, prípadne `docs/05-bezpecnost.md` |
| Plán produkcie | `docs/04-produkcia.md` |
| Kód, štruktúra, známy problém | `docs/06-kod.md` |
| Nová chyba a jej riešenie | `docs/08-riesenie-problemov.md` |
| Inštalátor, `tiasbom.env`, hromadné nasadenie | `docs/09-instalacia.md` |
| Konkrétne stroje, adresy, projekty | `docs/local/environment.md`, nikdy tracked dokumenty |

Na konci každého chatu, v ktorom sa niečo zmenilo:

1. Aktualizuj `docs/STATUS.md` a dátum v jeho hlavičke.
2. Skontroluj, že dokumenty neodporujú kódu. Pri rozpore platí overené správanie, nie dokument.
3. Povedz používateľovi, čo si zmenil a čo ostalo neoverené.
