# Baby Name Picker

A Dockerized baby name explorer built with .NET 10, SQLite, and a mobile-friendly vanilla JS + Tailwind UI.

## Features

- Search names and nicknames with gender filters (Boy, Girl, Unisex, Any)
- Random name picker with the same gender filters
- Browse top 100 SSA names per year and gender
- Unisex classification with male-share slant indicator
- SSA data seeding from official US Social Security Administration rankings (1950–latest)

## Quick start (local)

```bash
cd D:\workspace\baby-name-picker
dotnet run --project src/BabyNamePicker
```

Open http://localhost:5000 (or the port shown in the console).

On first run with an empty database, the app seeds from SSA data. It looks for data in this order:

1. `data/names.zip` (official SSA archive — recommended for full 1950–latest history)
2. Automatic download from SSA (may be blocked in some environments)
3. Bundled `data/ssa-sample/` (limited years for development)

Download the official archive manually if needed:

```bash
# Save to data/names.zip, then run import
dotnet run --project src/BabyNamePicker -- import-ssa --from 1950 --top 100
```

## Bulk SSA import

```bash
dotnet run --project src/BabyNamePicker -- import-ssa --from 1880 --to 2024 --top 100
```

Options:

- `--from YEAR` — start year (default 1950)
- `--to YEAR` — end year (default: latest in zip)
- `--top N` — top N names per sex per year (default 100)
- `--zip PATH` — use a local `names.zip` instead of downloading
- `--dir PATH` — import from a folder of `yobYYYY.txt` files

Admin API (POST): `/api/admin/import-ssa` with optional JSON body matching `SsaImportOptions`.

## Docker

Create the shared network once:

```bash
docker network create workspace
```

Build and run:

```bash
docker compose up --build
```

The app listens on port **8080**. SQLite data is stored in the `baby-name-picker-data` volume at `/data/babynames.db`.

Service and container names are prefixed (`baby-name-picker`) to avoid collisions on the shared `workspace` network.

## Data model

- **Names** — canonical name, gender, male-share slant
- **Nicknames** — many-to-many with names
- **NameYearStats** — rank and count per name/year/sex

Unisex names are classified when the minority gender share is at least 15% across imported SSA data.

## Project layout

```
baby-name-picker/
  src/BabyNamePicker/     # ASP.NET Core app
  data/nicknames.json     # curated nickname seed data
  Dockerfile
  docker-compose.yml
```
