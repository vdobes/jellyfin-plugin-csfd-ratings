# Contributing

Thanks for helping improve ČSFD Ratings for Jellyfin. Small, focused changes are easiest to review.

## Before opening an issue

- Search existing issues first.
- Use the appropriate issue template.
- Remove API keys, paths, usernames, IP addresses, and other private data from logs.
- Include the Jellyfin version, plugin version, installation type, and relevant log excerpt.

For security vulnerabilities, use the private process described in [SECURITY.md](SECURITY.md).

## Development setup

Requirements:

- .NET 9 SDK
- Jellyfin 10.11-compatible packages (restored through NuGet)
- Docker only when testing the optional `node-csfd-api` sidecar

```bash
git clone https://github.com/vdobes/jellyfin-plugin-csfd-ratings.git
cd jellyfin-plugin-csfd-ratings
dotnet restore Jellyfin.Plugin.CsfdRatings.sln
dotnet test Jellyfin.Plugin.CsfdRatings.sln -c Release
./build.sh --zip
```

## Pull requests

1. Create a branch from `main`.
2. Keep network access out of metadata providers; only the synchronization service may fetch data.
3. Preserve the conservative matching policy: ambiguous results must require review.
4. Add or update tests for behavioral changes.
5. Run the Release test suite and verify `git diff --check`.
6. Explain user-visible behavior and migration impact in the pull request.

Do not commit build output, caches, credentials, real API responses containing private data, or a populated plugin configuration.

## Versioning and releases

Versions use four numeric parts, for example `1.0.2.0`. Maintainers update `meta.json` and the project assembly versions, merge the change, then push a matching `v1.0.2.0` tag. GitHub Actions builds the tagged commit and updates the Jellyfin repository manifest.

## License

By submitting a contribution, you agree that it is licensed under [GPL-3.0-or-later](LICENSE).
