# Changelog

All notable user-visible changes are documented here. Versions use Jellyfin's four-part numbering scheme.

## [1.0.1.0] - 2026-08-05

### Added

- Native ČSFD movie ratings through Jellyfin's `CommunityRating` field.
- Conservative automatic matching with manual review and ČSFD external IDs.
- Persistent cache, rating TTL, retry backoff, request pacing, and weekly request budget.
- Dry-run mode, new-item synchronization, CSV review report, and restoration of original ratings.
- Optional sidecar API-key authentication.
- Czech and English documentation, automated builds, tests, releases, and Jellyfin repository manifest generation.

### Fixed

- Cached dry-run results are applied on the next live run without another network lookup.
- New-item processing respects configured library scope and persists request usage.
- Concurrent scheduled and new-item runs are serialized.
- Malformed sidecar responses are retried instead of cached as missing titles.
- Release artifacts are built from the tagged commit.

[1.0.1.0]: https://github.com/vdobes/jellyfin-plugin-csfd-ratings/releases/tag/v1.0.1.0
