# Baby Name Picker

A Dockerized baby name explorer built with .NET 10, SQLite, and a mobile-friendly vanilla JS + Tailwind UI.

## Features

- Search names and nicknames with gender and popularity filters (Boy, Girl, Unisex, Any; Top 100 / 101–500 / 501+)
- Random name picker with the same filters
- Browse SSA names per year and gender (top 100, 500, or 1000)
- Unisex classification with male-share slant indicator
- SSA data seeding from official US Social Security Administration rankings (1880–latest, top 1000 per sex by default)
- LLM enrichment for meanings, origins, nicknames, and themes (Ollama or OpenAI)

## Quick start (local)

```bash
cd D:\workspace\baby-name-picker
dotnet run --project src/BabyNamePicker/BabyNamePicker.csproj
```

Open http://localhost:5000 (or the port shown in the console).

On first run with an empty database, the app seeds from SSA data. It looks for data in this order:

1. `data/names.zip` (official SSA archive — recommended for full 1950–latest history)
2. Automatic download from SSA (may be blocked in some environments)
3. Bundled `data/ssa-sample/` (limited years for development)

Download the official archive manually if needed:

```bash
# Save to data/names.zip, then run import
dotnet run --project src/BabyNamePicker/BabyNamePicker.csproj -- import-ssa --from 1880 --top 1000
```

## Bulk SSA import

```bash
dotnet run --project src/BabyNamePicker/BabyNamePicker.csproj -- import-ssa --from 1880 --to 2024 --top 1000
```

Options:

- `--from YEAR` — start year (default 1880)
- `--to YEAR` — end year (default: latest in zip)
- `--top N` — top N names per sex per year (default 1000)
- `--zip PATH` — use a local `names.zip` instead of downloading
- `--dir PATH` — import from a folder of `yobYYYY.txt` files

Admin API (POST): `/api/admin/import-ssa` with optional JSON body matching `SsaImportOptions`.

## LLM enrichment

Configure in `appsettings.json` or environment variables:

```json
{
  "Llm": {
    "Provider": "Ollama",
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2",
    "ApiKey": ""
  }
}
```

Set `Provider` to `OpenAI` and provide `ApiKey` for cloud enrichment. `Model` defaults work with `gpt-4o-mini` for OpenAI.

Batch enrich names (meanings, origins, nicknames, themes):

```bash
dotnet run --project src/BabyNamePicker/BabyNamePicker.csproj -- enrich-names --batch 25
```

Options:

- `--batch N` — names per run (default 25)
- `--provider Ollama|OpenAI` — override configured provider
- `--force` — re-enrich names that already have metadata

Admin API (POST): `/api/admin/enrich-names` with optional JSON body `{ "batchSize": 25, "force": false }`.

Check progress: `GET /api/admin/enrichment-status`.

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
- **NameMetadata** — LLM-enriched meaning, origin, pronunciation, themes, variants

Unisex names are classified when the minority gender share is at least 15% across imported SSA data.

## Project layout

```
baby-name-picker/
  src/
    BabyNamePicker.slnx
    BabyNamePicker/       # ASP.NET Core app
  data/nicknames.json     # curated nickname seed data
  Dockerfile
  docker-compose.yml
```
