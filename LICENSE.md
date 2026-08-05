# License

GNU General Public License v3.0 or later (`GPL-3.0-or-later`).

The full license text belongs in a file named `LICENSE`:

```bash
curl -o LICENSE https://www.gnu.org/licenses/gpl-3.0.txt
git rm LICENSE.md && git add LICENSE && git commit -m "Add license text"
```

Until `LICENSE` exists, GitHub will not detect the license and the GPL's
distribution terms are not formally satisfied.

```
Copyright (C) 2026 venca

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see <https://www.gnu.org/licenses/>.
```

GPL-3.0 applies because the project builds on the
[Jellyfin plugin template](https://github.com/jellyfin/jellyfin-plugin-template),
which is GPL-3.0. Copyleft only applies on distribution — private use carries no
obligation.
