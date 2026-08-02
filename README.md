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

1. `data/names.zip` (official SSA archive — recommended for full 1880–latest history)
2. Automatic download from SSA (may be blocked in some environments)
3. Bundled `data/ssa-sample/` (limited years for development)

See [SSA data download](#ssa-data-download) for the official URL and `curl` commands.

## SSA data download

Baby names come from the [US Social Security Administration](https://www.ssa.gov/oact/babynames/) baby name popularity dataset. The app expects the official **`names.zip`** archive, which contains one `yobYYYY.txt` file per year (`Name,Sex,Count` per line).

**Download URL:** https://www.ssa.gov/oact/babynames/names.zip

Save it to `data/names.zip` in the repo root (create `data/` if needed), then import:

```bash
dotnet run --project src/BabyNamePicker/BabyNamePicker.csproj -- import-ssa --from 1880 --top 1000
```

**curl (Linux, macOS, Git Bash):**

```bash
mkdir -p data
curl -fL -o data/names.zip https://www.ssa.gov/oact/babynames/names.zip
```

**PowerShell:**

```powershell
New-Item -ItemType Directory -Force -Path data | Out-Null
curl.exe -fL -o data/names.zip https://www.ssa.gov/oact/babynames/names.zip
```

If automatic download works in your environment, you can skip the manual step — the importer will fetch `names.zip` on first run when the file is missing.

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

Configure via the repo-root `.env` (recommended), `appsettings.json`, or environment variables.

`.env` (see `.env.example`):

```env
LLM_PROVIDER=Ollama
LLM_BASE_URL=http://ollama:11434
LLM_MODEL=llama3.2
LLM_API_KEY=
```

- **Docker:** Compose maps these to `Llm__*` for the container. Use `http://ollama:11434` when Ollama is on the shared Docker network.
- **Local:** `dotnet run` loads `.env` from the repo root (existing env vars win). Use `http://localhost:11434` if Ollama is on the host.

Equivalent `appsettings.json`:

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

Set `Provider` / `LLM_PROVIDER` to `OpenAI` and provide `ApiKey` / `LLM_API_KEY` for cloud enrichment. `Model` defaults work with `gpt-4o-mini` for OpenAI.

Batch enrich names (meanings, origins, nicknames, themes):

```bash
dotnet run --project src/BabyNamePicker/BabyNamePicker.csproj -- enrich-names --batch 25
```

Docker (container must be running):

```bash
./enrich-names.sh --batch 50
.\enrich-names.ps1 --batch 50
./enrich-names.sh --all --batch 50   # repeat until nothing left
```

Options:

- `--batch N` — names per run (default 25)
- `--provider Ollama|OpenAI` — override configured provider
- `--force` — re-enrich names that already have metadata
- `--all` — (scripts only) keep running batches until no names remain

Admin API (POST): `/api/admin/enrich-names` with optional JSON body `{ "batchSize": 25, "force": false }`.

Check progress: `GET /api/admin/enrichment-status`.

## Docker

Copy env defaults and create the shared Docker network (once):

```bash
cp .env.example .env   # or: copy .env.example .env
./setup.sh             # or: .\setup.ps1
```

`DOCKER_SHARED_NETWORK` in `.env` names the external network (default `workspace`). Setup creates that network if it does not exist.

Build and run:

```bash
docker compose up --build
```

The app listens on port **8080**. SQLite data is stored in the `baby-name-picker-data` volume at `/data/babynames.db`.

Service and container names are prefixed (`baby-name-picker`) to avoid collisions on the shared network.

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
  .env.example            # Docker network + LLM config template
  setup.ps1 / setup.sh    # create .env + shared Docker network
  enrich-names.ps1 / .sh  # run LLM enrichment in the Docker container
  Dockerfile
  docker-compose.yml
```

