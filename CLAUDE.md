# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 8 background service that polls a Dolibarr ERP instance for pending print jobs and sends them to a local CUPS printer. It runs as a Docker container on the same host as the printer.

## Build & run commands

```bash
# Build and run via Docker Compose (normal deployment)
docker compose up --build -d

# View logs
docker compose logs -f print-client

# Stop
docker compose down

# Build the .NET project locally (without Docker)
cd PrintClient
dotnet build
dotnet run
```

There are no tests in this project.

## Architecture

The entire application is a single file: [PrintClient/Program.cs](PrintClient/Program.cs).

**Flow (every 30 seconds):**
1. `GET /printjobapi/printjobs?sqlfilters=status%3A%3D%3A0` — fetches jobs with status=0 (pending)
2. `GET /documents/download?original_file=...&modulepart=...` — downloads the file as base64-encoded JSON
3. Decodes and writes to a temp file, then calls `lp -d AL-C2800 <file>` via subprocess
4. On success → `PUT /printjobapi/printjobs/{id}?status=1`; on failure → status=99

**Key configuration hardcoded in `Program.cs`:**
- `DOLAPIKEY` header value (line 49) — the Dolibarr REST API key
- `ApiUrl` (line 53) — uses `host.docker.internal` to reach the host's Dolibarr instance
- Printer name `AL-C2800` (line 145) — passed to `lp -d`

**Docker setup:**
- The container mounts `/var/run/cups/cups.sock` from the host, so `lp` commands inside the container talk to the host's CUPS daemon
- `./temp_data` is mounted to `/app/temp` for optional log/file persistence
- `host.docker.internal` resolves to the host via `extra_hosts: host-gateway`

## Dolibarr API models

- `PrintTask`: maps the print job record (`Id`, `FileName`, `Modulepart`, `JobId`, `Status`)
- `DolibarrDownload`: maps the document download response (`Content` is base64, `Encoding`, `ContentType`, etc.)

## Notes

- SSL certificate validation is disabled globally (`ServerCertificateCustomValidationCallback = true`) to support self-signed certs on the Dolibarr host
- The `printjobapi` endpoint is a custom Dolibarr module, not part of standard Dolibarr
