<div align="center">

# ČSFD Ratings pro Jellyfin

**Hodnocení z ČSFD přímo v Jellyfinu — ve všech klientech, bez zásahů do webového rozhraní.**

[![Build](https://github.com/vdobes/jellyfin-csfd-rating-scrapper/actions/workflows/build.yml/badge.svg)](https://github.com/vdobes/jellyfin-csfd-rating-scrapper/actions/workflows/build.yml)
[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11-00A4DC)](https://jellyfin.org)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

[English](README.en.md) · [Instalace](#instalace) · [Nastavení](#nastavení) · [Párování](#jak-funguje-párování) · [Řešení problémů](#řešení-problémů)

</div>

---

Jellyfin stahuje hodnocení z TMDb nebo IMDb. U českých a slovenských filmů bývá mimo — a i u zahraničních často neodpovídá tomu, jak film vidí české publikum. Tenhle plugin nahradí komunitní hodnocení tím z [ČSFD](https://www.csfd.cz).

Zapisuje ho do standardního pole `CommunityRating`, takže se objeví **všude**: ve webu, na Android TV, v Infuse, ve Findroidu. Žádný JavaScript, žádné překrývání posterů, žádná závislost na dalších pluginech.

## Co plugin dělá a co ne

**Jen filmy.** Seriály zatím ne — ČSFD je dělí na řady a epizody a spolehlivé párování by chtělo vlastní logiku.

**Nehádá.** Když si není jistý, film nechá být a označí ho k ručnímu párování. Špatné hodnocení je horší než žádné.

**Nepřepisuje nenávratně.** Původní hodnotu si uloží a jedním tlačítkem ji vrátíš.

**Nechová se jako crawler.** Prodleva mezi dotazy, týdenní strop, a při prvním náznaku omezení ze strany serveru běh ukončí a nechá stávající data být.

**Nezamrzne.** Hodnocení má platnost, po jejímž uplynutí se načte znovu. Nové filmy se doplní samy po skenu knihovny.

## Jak to funguje

```
Jellyfin ──► CsfdRatingProvider     po refreshi metadat vrátí hodnocení z cache
         └─► naplánovaná úloha      jediné místo, které chodí na síť
                     │
                     ▼
             http://csfd-api:3000   node-csfd-api ve vnitřní Docker síti
                     │
                     ▼
                  csfd.cz
```

Plugin sám na ČSFD nechodí. Data bere z [node-csfd-api](https://github.com/bartholomej/node-csfd-api), který běží jako samostatný kontejner vedle Jellyfinu. Ten řeší parsování stránek i proof-of-work ochranu, kterou ČSFD nasadilo — když se změní HTML, aktualizuje se obraz kontejneru a plugin zůstane netknutý.

Načítání a zápis jsou oddělené. Provider běží po každém refreshi metadat a jen znovu použije, co je v cache, takže když TMDb přepíše `CommunityRating`, další průchod to srovná bez jediného dotazu ven.

---

## Instalace

### Přes repozitář pluginů

**Ovládací panel → Pluginy → Repozitáře → +**

| | |
|---|---|
| Název | `ČSFD Ratings` |
| URL | `https://raw.githubusercontent.com/vdobes/jellyfin-csfd-rating-scrapper/main/manifest.json` |

Pak **Katalog → ČSFD Ratings → Instalovat** a restart Jellyfinu. Aktualizace už chodí samy.

### Ručně

Stáhni `.zip` z [Releases](https://github.com/vdobes/jellyfin-csfd-rating-scrapper/releases) a rozbal do složky s pluginy:

```bash
mkdir -p /cesta/k/jellyfin/config/plugins/CsfdRatings_1.0.1.0
unzip csfd-ratings_1.0.1.0.zip -d /cesta/k/jellyfin/config/plugins/CsfdRatings_1.0.1.0/
chown -R 1000:1000 /cesta/k/jellyfin/config/plugins/CsfdRatings_1.0.1.0
```

Ve složce musí ležet `Jellyfin.Plugin.CsfdRatings.dll` i `meta.json`, ne v podadresáři.

### Sidecar

Bez něj plugin nemá odkud brát data. Do svého `docker-compose.yml`:

```yaml
  csfd-api:
    image: bartholomej/node-csfd-api:5.11.0
    container_name: csfd-api
    restart: unless-stopped
    networks: [media_net]           # stejná síť jako Jellyfin
    environment:
      API_KEY: "${CSFD_API_KEY}"    # volitelné
    healthcheck:
      test: ["CMD", "node", "-e", "fetch('http://127.0.0.1:3000/movie/2294',{headers:{'x-api-key':process.env.API_KEY||''}}).then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]
      interval: 5m
      timeout: 15s
      start_period: 30s
```

> **Nepublikuj jeho port.** Služba nemá vlastní omezení počtu dotazů. Kdyby se k ní dostal kdokoli z internetu, potečou dotazy na ČSFD z tvojí IP adresy. Nech ji na vnitřní síti — Jellyfin ji najde pod jménem služby.

Když nastavíš `API_KEY`, sidecar začne vyžadovat hlavičku `x-api-key`. Stejnou hodnotu vlož do nastavení pluginu.

Ověření, že spolu mluví:

```bash
docker compose up -d csfd-api
docker run --rm --network $(docker inspect csfd-api -f '{{range $k,$v := .NetworkSettings.Networks}}{{$k}}{{end}}') \
  busybox wget -qO- http://csfd-api:3000/movie/2294 | head -c 120
```

---

## První spuštění

Přepis hodnocení je hromadná změna databáze. **Nejdřív záloha:** Ovládací panel → Zálohy → Vytvořit zálohu.

Pak na stránce pluginu:

1. **Plugin zapnutý** ✓ a **Zkušební režim** ✓
2. **Max. položek na jeden běh** na `20`
3. **Test připojení** — musí vrátit hodnocení Vykoupení z věznice Shawshank
4. **Načíst chybějící hodnocení**
5. Zkontrolovat log:

```bash
docker logs jellyfin 2>&1 | grep ČSFD | tail -20
```

Ve zkušebním režimu uvidíš řádky `DRY RUN {film}: CommunityRating 6.4 -> 8.7` a nic se nezapíše. Když titul i rok sedí, zkušební režim vypni, limit dej na `0` a spusť znovu.

---

## Nastavení

| Volba | Výchozí | K čemu |
|---|---|---|
| Adresa sidecaru | `http://csfd-api:3000` | jméno služby na vnitřní síti |
| API klíč sidecaru | prázdné | jen když má `csfd-api` nastavené `API_KEY` |
| Prodleva mezi dotazy | 2000 ms | níž nechoď |
| Týdenní limit dotazů | 2000 | klouzavé okno sedmi dní, `0` vypne |
| Platnost hodnocení | 90 dní | po vypršení se načte znovu |
| Opakovat nenalezené po | 7 dní | nebo hned po úpravě metadat |
| Max. položek na jeden běh | 0 | pro první backfill nastav `20` |
| Tolerance roku ±1 | zapnuto | jen při shodě režiséra |
| Uchovat původní hodnocení | zapnuto | **nevypínej**, viz [Návrat zpět](#návrat-zpět) |
| Nové filmy po scanu | zapnuto | s odstupem tří minut |
| Ruční párování | prázdné | `ItemId = csfdId`, jeden pár na řádek |

### Kolik to spotřebuje dotazů

Jeden film stojí 1–3 dotazy. Pro knihovnu o 300 filmech:

- **první backfill** ≈ 600 dotazů, při dvousekundové prodlevě zhruba 20 minut
- **běžný provoz** s platností 90 dní ≈ 50 dotazů týdně

Výchozí limit 2000 na to bohatě stačí.

---

## Ovládání

Všechno je na stránce pluginu — nahoře přehled stavu, pod ním tlačítka:

| | |
|---|---|
| **Test připojení** | jeden dotaz na sidecar, výsledek hned |
| **Načíst chybějící hodnocení** | zařadí úlohu, průběh v Naplánovaných úlohách |
| **Vypsat nespárované do CSV** | `csfd-review.csv` v datové složce pluginu |
| **Zkusit nespárované znovu** | zahodí neúspěchy, další běh je zkusí znovu |
| **Obnovit původní hodnocení** | vrátí `CommunityRating` do stavu před pluginem |

---

## Jak funguje párování

ČSFD nevrací TMDb ani IMDb identifikátory, takže shodu není čím ověřit. Kaskáda je proto přísná:

1. **ruční přiřazení** z nastavení
2. **uložené ČSFD ID** u filmu — hotovo, nehledá se
3. hledání podle originálního názvu, pak podle českého
4. přijme se **jediný** kandidát s přesnou shodou názvu a roku
5. rok ±1 **jen** při přesné shodě režiséra
6. cokoli jiného skončí jako **k ručnímu párování**

Názvy se porovnávají bez diakritiky a interpunkce, takže „Vykoupení z věznice Shawshank" a „vykoupeni z veznice shawshank" jsou totéž.

### Ruční doplnění

Nespárovaný film opravíš přímo v Jellyfinu:

**Film → Upravit metadata → ČSFD → vlož ID → Uložit**

ČSFD ID je číslo v URL filmu: `csfd.cz/film/`**`2294`**`-vykoupeni-z-veznice-shawshank/`. Na stránce filmu pak přibude odkaz na ČSFD a při dalším běhu se uložené ID použije rovnou.

Druhá možnost je pole **Ruční párování** v nastavení, kam patří dvojice `ItemId = csfdId`. ID kandidátů najdeš v tabulce nespárovaných filmů.

---

## Návrat zpět

Jellyfin si historii `CommunityRating` nedrží a to pole nejde zamknout. Plugin proto před prvním zápisem uloží původní hodnotu k filmu a tlačítko **Obnovit původní hodnocení** ji vrátí.

> **Obnovu spusť dřív, než plugin odinstaluješ.** Po smazání pluginu se původní hodnoty vrátit nedají.

Vypnutím volby *Uchovat původní hodnocení* se přepis stane nevratným. Je to podporované, ale je dobré o tom vědět.

---

## Řešení problémů

<details>
<summary><strong>Plugin není v seznamu pluginů</strong></summary>

```bash
docker logs jellyfin 2>&1 | grep -iE 'plugin|csfd' | tail -20
```

Ve složce musí být `.dll` i `meta.json` přímo, ne v podadresáři, a vlastník souborů musí odpovídat uživateli, pod kterým Jellyfin běží.

Pokud se v logu objeví jiná verze, než jsi nainstaloval, máš ve složce pluginů víc verzí najednou — Jellyfin načte jednu a ostatní zahodí.
</details>

<details>
<summary><strong>Test připojení selže</strong></summary>

```bash
docker compose ps csfd-api
docker logs --tail 30 csfd-api
```

Nejčastěji: sidecar není na stejné síti jako Jellyfin, v adrese je jiné jméno služby, nebo má nastavené `API_KEY` a plugin ho nezná.
</details>

<details>
<summary><strong>Hodnocení se nezapisují</strong></summary>

Projdi v tomhle pořadí: zkušební režim, týdenní limit, zapnutý plugin. Všechno je vidět v přehledu stavu.
</details>

<details>
<summary><strong>Filmů v knihovně: 0</strong></summary>

Plugin bere jen položky typu Film. Filmy v knihovně označené jako Seriály nebo Smíšený obsah nevidí.
</details>

<details>
<summary><strong>Hodnocení po refreshi metadat zmizí</strong></summary>

Nemělo by — provider ho po každém refreshi vrátí z cache. Když se to děje, pošli prosím kus logu do [Issues](https://github.com/vdobes/jellyfin-csfd-rating-scrapper/issues).
</details>

---

## Sestavení ze zdrojů

Potřebuješ .NET 9 SDK.

```bash
git clone https://github.com/vdobes/jellyfin-csfd-rating-scrapper.git
cd jellyfin-csfd-rating-scrapper
dotnet test Jellyfin.Plugin.CsfdRatings.sln
./build.sh --zip
```

Výsledek je v `dist/`. Nová verze se vydává tagem:

```bash
git tag v1.0.2.0 && git push origin v1.0.2.0
```

Pipeline postaví plugin z commitu tagu, spustí testy, vytvoří Release a doplní verzi do `manifest.json`.

Chyby a nápady patří do [Issues](https://github.com/vdobes/jellyfin-csfd-rating-scrapper/issues), pull requesty jsou vítané.

---

## Poděkování

Kostra projektu vychází z [oficiální šablony pluginu](https://github.com/jellyfin/jellyfin-plugin-template) (GPL-3.0). Uspořádání cache a naplánovaných úloh se inspirovalo pluginem [MDBList Ratings](https://github.com/Druidblack/Jellyfin.Plugin.MDBList_Ratings) (GPL-3.0).

Data poskytuje [node-csfd-api](https://github.com/bartholomej/node-csfd-api) od Lukáše Bartáka (MIT). Běží jako samostatná služba, jeho kód není součástí pluginu.

Existuje také [jellyfin-csfd-rating](https://github.com/007hacky007/jellyfin-csfd-rating), který řeší podobný problém překrytím posterů ve webovém rozhraní. Tenhle plugin s ním nesdílí zdrojový kód a jde jinou cestou — zápisem do nativního pole, aby bylo hodnocení vidět i mimo prohlížeč.

Hodnocení pochází z [ČSFD.cz](https://www.csfd.cz). Projekt s ČSFD nijak nesouvisí a je určený pro soukromé nekomerční použití.

## Licence

[GPL-3.0-or-later](LICENSE)
