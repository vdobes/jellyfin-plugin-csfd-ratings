<div align="center">

# ČSFD Ratings for Jellyfin

**Czech-Slovak film database ratings in Jellyfin — on every client, with no changes to the web UI.**

[![Build](https://github.com/vdobes/jellyfin-csfd-rating-scrapper/actions/workflows/build.yml/badge.svg)](https://github.com/vdobes/jellyfin-csfd-rating-scrapper/actions/workflows/build.yml)
[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11-00A4DC)](https://jellyfin.org)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

[Česky](README.md) · [Install](#install) · [Settings](#settings) · [Matching](#how-matching-works) · [Troubleshooting](#troubleshooting)

</div>

---

Jellyfin pulls ratings from TMDb or IMDb. For Czech and Slovak films those are often wrong, and even for international titles they rarely reflect how Czech audiences actually rate a film. This plugin replaces the community rating with the one from [ČSFD](https://www.csfd.cz).

It writes into the standard `CommunityRating` field, so the value shows up **everywhere**: web, Android TV, Infuse, Findroid. No JavaScript, no poster overlays, no dependency on other plugins.

## What it does, and what it doesn't

**Movies only.** Series are out of scope for now — ČSFD splits them into seasons and episodes and matching them reliably needs its own logic.

**It never guesses.** When a match is ambiguous the film is left alone and flagged for manual review. A wrong rating is worse than no rating.

**It is reversible.** The previous value is stored before the first write and one button puts it back.

**It does not behave like a crawler.** Fixed delay between requests, a weekly cap, and at the first sign of rate limiting the run stops and leaves existing data untouched.

**It does not go stale.** Ratings expire and get refetched. New films are picked up automatically after a library scan.

## How it works

```
Jellyfin ──► CsfdRatingProvider     reapplies the cached rating after a metadata refresh
         └─► scheduled task         the only thing that touches the network
                     │
                     ▼
             http://csfd-api:3000   node-csfd-api on the internal Docker network
                     │
                     ▼
                  csfd.cz
```

The plugin never talks to ČSFD directly. Data comes from [node-csfd-api](https://github.com/bartholomej/node-csfd-api) running as a sidecar container. It handles both HTML parsing and the proof-of-work challenge ČSFD deploys — when the markup changes, you bump an image tag and the plugin is unaffected.

Fetching and writing are separate concerns. The provider runs after every metadata refresh and only reuses what is already cached, so when TMDb overwrites `CommunityRating` the next pass corrects it without a single outbound request.

---

## Install

### Via plugin repository

**Dashboard → Plugins → Repositories → +**

| | |
|---|---|
| Name | `ČSFD Ratings` |
| URL | `https://raw.githubusercontent.com/vdobes/jellyfin-csfd-rating-scrapper/main/manifest.json` |

Then **Catalogue → ČSFD Ratings → Install** and restart. Updates arrive automatically.

### Manually

Download the `.zip` from [Releases](https://github.com/vdobes/jellyfin-csfd-rating-scrapper/releases):

```bash
mkdir -p /path/to/jellyfin/config/plugins/CsfdRatings_1.0.1.0
unzip csfd-ratings_1.0.1.0.zip -d /path/to/jellyfin/config/plugins/CsfdRatings_1.0.1.0/
chown -R 1000:1000 /path/to/jellyfin/config/plugins/CsfdRatings_1.0.1.0
```

Both `Jellyfin.Plugin.CsfdRatings.dll` and `meta.json` must sit directly in that folder.

### Sidecar

Without it the plugin has no data source. Add to your `docker-compose.yml`:

```yaml
  csfd-api:
    image: bartholomej/node-csfd-api:5.11.0
    container_name: csfd-api
    restart: unless-stopped
    networks: [media_net]           # same network as Jellyfin
    environment:
      API_KEY: "${CSFD_API_KEY}"    # optional
    healthcheck:
      test: ["CMD", "node", "-e", "fetch('http://127.0.0.1:3000/movie/2294',{headers:{'x-api-key':process.env.API_KEY||''}}).then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]
      interval: 5m
      timeout: 15s
      start_period: 30s
```

> **Do not publish its port.** The service has no rate limiting of its own. If it were reachable from the internet, anyone could scrape ČSFD through your IP address. Keep it on the internal network — Jellyfin finds it by service name.

Setting `API_KEY` makes the sidecar require an `x-api-key` header. Put the same value in the plugin settings.

---

## First run

Overwriting ratings is a bulk database change. **Back up first:** Dashboard → Backups → Create backup.

1. Tick **Plugin enabled** and **Dry run**
2. Set **Max items per run** to `20`
3. **Test connection** — should return the rating for The Shawshank Redemption
4. **Fetch missing ratings**
5. Check the log:

```bash
docker logs jellyfin 2>&1 | grep ČSFD | tail -20
```

Dry run prints `DRY RUN {film}: CommunityRating 6.4 -> 8.7` and writes nothing. Once the titles and years look right, turn dry run off, set the limit to `0` and run again.

---

## Settings

| Option | Default | Notes |
|---|---|---|
| Sidecar URL | `http://csfd-api:3000` | service name on the internal network |
| Sidecar API key | empty | only if `csfd-api` has `API_KEY` set |
| Delay between requests | 2000 ms | do not go lower |
| Weekly request limit | 2000 | rolling seven day window, `0` disables |
| Rating lifetime | 90 days | refetched after it expires |
| Retry not found after | 7 days | or immediately after a metadata edit |
| Max items per run | 0 | set `20` for the first backfill |
| Year tolerance ±1 | on | only with a matching director |
| Preserve original rating | on | **leave this on**, see [Rolling back](#rolling-back) |
| New films after scan | on | three minutes after the scan settles |
| Manual matches | empty | `ItemId = csfdId`, one pair per line |

A single film costs 1–3 requests. For a 300 film library the initial backfill is roughly 600 requests (about 20 minutes at a two second delay); steady state with a 90 day lifetime is around 50 requests per week.

---

## Controls

Everything lives on the plugin page — status at the top, buttons below:

| | |
|---|---|
| **Test connection** | one request to the sidecar, result shown inline |
| **Fetch missing ratings** | queues the task, progress under Scheduled Tasks |
| **Export unmatched to CSV** | `csfd-review.csv` in the plugin data folder |
| **Retry unmatched** | clears failures so the next run tries again |
| **Restore original ratings** | puts `CommunityRating` back to its pre-plugin value |

---

## How matching works

ČSFD exposes no TMDb or IMDb identifiers, so there is nothing to verify a match against. The cascade is deliberately strict:

1. **manual assignment** from settings
2. **stored ČSFD ID** on the item — done, no search
3. search by original title, then by the localised one
4. accept a **single** candidate with an exact title and year match
5. year off by one **only** with an exact director match
6. anything else is flagged **for manual review**

Titles are compared with diacritics and punctuation stripped, so "Vykoupení z věznice Shawshank" and "vykoupeni z veznice shawshank" are the same string.

To fix an unmatched film: **Item → Edit metadata → ČSFD → paste the ID → Save**. The ID is the number in the ČSFD URL: `csfd.cz/film/`**`2294`**`-vykoupeni-z-veznice-shawshank/`.

---

## Rolling back

Jellyfin keeps no history of `CommunityRating` and the field cannot be locked. The plugin stores the previous value on the item before its first write, and **Restore original ratings** puts it back.

> **Run the restore before uninstalling.** Once the plugin is gone the original values cannot be recovered.

Turning off *Preserve original rating* makes the overwrite permanent. Supported, but worth knowing.

---

## Troubleshooting

<details>
<summary><strong>Plugin does not appear in the list</strong></summary>

```bash
docker logs jellyfin 2>&1 | grep -iE 'plugin|csfd' | tail -20
```

Both files must sit directly in the plugin folder, owned by the user Jellyfin runs as. If the log shows a different version than you installed, you have more than one version in the plugins folder — Jellyfin loads one and discards the rest.
</details>

<details>
<summary><strong>Test connection fails</strong></summary>

```bash
docker compose ps csfd-api
docker logs --tail 30 csfd-api
```

Usually: the sidecar is not on the same network as Jellyfin, the URL uses a different service name, or `API_KEY` is set and the plugin does not know it.
</details>

<details>
<summary><strong>Nothing is being written</strong></summary>

Check in this order: dry run, weekly limit, plugin enabled. All three are visible in the status panel.
</details>

<details>
<summary><strong>Movies in library: 0</strong></summary>

Only items of type Movie are considered. Films in a library typed as Shows or Mixed content are invisible to the plugin.
</details>

---

## Building from source

Requires the .NET 9 SDK.

```bash
git clone https://github.com/vdobes/jellyfin-csfd-rating-scrapper.git
cd jellyfin-csfd-rating-scrapper
dotnet test Jellyfin.Plugin.CsfdRatings.sln
./build.sh --zip
```

Releases are cut by tag:

```bash
git tag v1.0.2.0 && git push origin v1.0.2.0
```

The pipeline builds from the tagged commit, runs the tests, creates a Release and appends the version to `manifest.json`.

Issues and pull requests are welcome.

---

## Credits

Built on the [official plugin template](https://github.com/jellyfin/jellyfin-plugin-template) (GPL-3.0). The cache and scheduled task layout took inspiration from [MDBList Ratings](https://github.com/Druidblack/Jellyfin.Plugin.MDBList_Ratings) (GPL-3.0).

Data comes from [node-csfd-api](https://github.com/bartholomej/node-csfd-api) by Lukáš Barták (MIT), running as a separate service — none of its code ships with the plugin.

[jellyfin-csfd-rating](https://github.com/007hacky007/jellyfin-csfd-rating) solves a similar problem by overlaying poster cards in the web UI. This plugin shares no source code with it and takes a different route, writing to the native field so ratings are visible outside the browser too.

Ratings come from [ČSFD.cz](https://www.csfd.cz). This project is not affiliated with ČSFD and is intended for private, non-commercial use.

## License

[GPL-3.0-or-later](LICENSE)
