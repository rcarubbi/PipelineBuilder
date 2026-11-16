# Pipeline Builder

A local POC for building linear data pipelines with React and Blockly, then running immutable JSON definition snapshots in .NET.

```text
frontend/    React + TypeScript + Vite; Vitest tests
backend/     .NET 10; Presentation / Application / Domain / Infrastructure
data/input/  Existing server CSV files (sample included)
data/output/ Completed Parquet files (ignored by Git)
```

## Run locally

Requires .NET 10 SDK, Node 24, and Docker with its daemon running. Excel support uses the MIT-licensed ExcelReader.NET library. From this directory:

```powershell
docker compose up -d --wait
$env:ConnectionStrings__Pipelines = 'Host=localhost;Port=5432;Database=pipelines;Username=pipelines;Password=local-development-only'
dotnet run --project backend/src/PipelineBuilder.Presentation
```

In another terminal:

```powershell
cd frontend
npm ci
npm run dev -- --host localhost
```

- Pipeline list (default): <http://localhost:5173>
- New pipeline designer: <http://localhost:5173/?page=designer>
- API / Swagger: <http://localhost:5050/swagger>
- Liveness: <http://localhost:5050/health/live>
- Readiness (PostgreSQL and worker): <http://localhost:5050/health/ready>
- TickerQ dashboard (Development only): <http://localhost:5050/tickerq-dashboard>
- Execution history: <http://localhost:5173/?page=logs>

The connection string is local development configuration, not a production credential. Alternatively configure `ConnectionStrings:Pipelines` with `dotnet user-secrets` in the Presentation project. Do not commit credentials.

If the Volta npm PowerShell launcher is broken, invoke the installed `npm-cli.js` using `node`; this is an environment issue, not a project requirement.

## Try it

1. Open the pipeline list and choose the **+** icon to create a pipeline. The initial canvas reads `customers.csv`, keeps adults, and writes `customers.parquet`.
2. Select CSV to edit its schema or discover types from up to 100 records.
3. Use the Blockly toolbox to drag processing blocks into the chain. Select a map to configure output columns. Select a filter expression to change its column/operator/constant. Switch the canvas to **Pipeline JSON** with the code icon above it to inspect the read-only definition and environment values; switch back with the blocks icon. This preview can contain sensitive values.
4. Configure named environments in the collapsible **Environments** panel below the canvas (it opens automatically when something needs fixing): add environments and `NAME = value` variables with the + icons, or tick **Edit as JSON (advanced)** to edit the same data as JSON. Variable names must start with a letter or underscore. Invalid entries are flagged and block saving. `${INPUT_FILE}` and `${OUTPUT_FILE}` resolve using the selected environment. `${CURRENT_ENVIRONMENT}` always resolves to the selected environment name, even if a variable with that name is configured; it is also available as an environment text expression.
   To define a pipeline variable, use Blockly's **Variables** category: create a variable, connect a text expression to its **set** block, and use its getter in a path. Assign each variable only once; values are resolved when the run is queued. Use an environment variable block named `CURRENT_ENVIRONMENT` to insert the selected environment name into a text expression.
5. Validate, then **Save & run**. The completed sample has 3 input records and 2 output records.
6. Return to the pipeline list with the list icon. The pencil icon opens a saved pipeline directly in the designer, restoring its workspace and revision automatically. Existing bookmarked pipeline URLs still work.
7. Change the output filename before running again. Existing destinations are never overwritten.
8. Use **Import pipeline from JSON** on the pipeline list to import one JSON file, or the row's export icon to download a saved pipeline. Import always creates a new pipeline; name conflicts get an `(imported)` suffix. The designer also exports saved pipelines; save changes first. JSON includes environment values, so treat exported files as sensitive. Schedules, run history, IDs, and revisions are not included. A definition-only JSON file is also accepted and gets an automatically laid-out Blockly canvas. Each pipeline row has icons to run its saved revision (using the selected environment), view its execution logs, delete it, and open the designer. Hover over an icon for its hint; every icon has an accessible label.
9. Execution logs are scoped to the selected pipeline, newest first (25 per page). Successful runs show **OK** and row counts in green; failures show **Failure**, the reason, and the failing block in red. Use the refresh icon for updates. Save editor changes before leaving the designer.
10. Deletion requires confirmation and is blocked while executions are queued/running. It removes the pipeline from the list, the designer and any schedules, but preserves execution history, snapshots, and all generated files. There is no restore/purge interface in the POC.
11. Open **Schedules** with the clock icon on the pipeline list. Give a schedule a unique alias (letters, digits, `-`, `_`; case-insensitive) and choose every few minutes, hourly, daily, weekly or monthly — or tick **Edit cron expression directly** for any five-field cron expression. The editor shows a plain-language summary, the generated cron expression, and the next five run times in your local time. Schedule times are **UTC**.
12. Use a pipeline row's clock icon to add it to a schedule by alias and tick the environments the scheduled runs use. The same pipeline can run on one schedule in several environments (for example Schedule A → Job 1 in `dev` and in `test`), may belong to several schedules, and a schedule can trigger many pipelines. Unticking an environment removes only that one. Scheduled runs appear in the execution logs as `Trigger: Scheduled: <alias>`.

Paths are relative to the configured backend input/output roots, not the browser filesystem. Absolute paths are allowed only within those roots. Configure roots via `Files:InputRoot` / `Files:OutputRoot`. Symlinks and reparse points are rejected; the data directories must be controlled by a trusted server administrator.

## Contracts and behavior

- Versioned controller API: `/api/v1/pipelines` (including `POST /api/v1/pipelines/import`), `/api/v1/schemas/discover`, `/api/v1/runs`, `/api/v1/schedules`.
- Pipeline JSON imports are limited to 1 MiB and validate the Blockly workspace against the executable definition. A mismatched workspace is rejected; definition-only JSON is rebuilt into Blockly blocks. Imports always create a new pipeline.
- Schedules: `GET /api/v1/schedules`, `PUT/DELETE /api/v1/schedules/{id}` (optimistic revision), `POST /api/v1/schedules/preview`, and `PUT/DELETE /api/v1/schedules/by-alias/{alias}/pipelines/{pipelineId}/environments/{environment}` (one association per pipeline and environment; adding an existing one is a no-op). Cron expressions are validated server-side with the same five-field parser TickerQ uses; the UI's checks are for convenience only.
- Each enabled schedule with at least one pipeline is registered as one TickerQ cron ticker (visible in the TickerQ dashboard). Editing, disabling, attaching or detaching updates that registration. PostgreSQL is the source of truth: TickerQ storage is in-memory, so registrations are rebuilt at startup and runs missed while the server was offline are not replayed.
- When a schedule fires, each attached pipeline's **current saved revision** is queued with an immutable snapshot for its chosen environment. A pipeline+environment that already has a queued or running execution is skipped (no backlog); other environments of the same pipeline still run. If a run cannot be created — for example, a missing environment variable — a failed execution with the reason is recorded so it is visible in the logs.
- A schedule with attached pipelines cannot be deleted; remove its pipelines first. Disabling pauses it without losing its pipelines.
- `GET /api/v1/pipelines?page=1` lists pipeline IDs, names, revisions and environment names only (25 per page). `DELETE /api/v1/pipelines/{id}?revision=1` soft-deletes that revision with concurrency protection.
- `GET /api/v1/runs?page=1&pipelineId={id}` returns execution metadata/results only, with bounded pagination; it never includes snapshot environment variables or source records. Omit `pipelineId` to see all history, including deleted pipelines. This is run history, not a raw server-log viewer.
- Workspace state and executable definitions are separate, with shared stable block IDs.
- One CSV, Parquet, Excel or API input, zero or more map/filter steps, and one CSV, Parquet or Excel output; no branching or scripts. Excel supports `.xls` (BIFF8) and `.xlsx` and requires a sheet name. Writing `.xls` buffers the workbook in memory. CSV, Parquet, Excel and API inputs support schema discovery with review before saving.
- API input accepts JSON/XML, GET/POST, nested data paths, HATEOAS `Link: rel="next"` or configured next paths, Basic/Bearer/API-key/OAuth Client Credentials authentication. Set credentials as `${secret.ENV_NAME}` references to server environment variables. Outbound API destinations resolving to private/local IPs are blocked unless their hostname is explicitly listed in `PipelineApi__AllowedHosts` (comma-separated). Requests cap responses at 10 MiB, 100 pages, 100,000 rows and 10 minutes; DTD/external XML entities and HTTP redirects are disabled.
- Text properties accept `${var.name}`, `${env.NAME}`, legacy `${NAME}` (environment), and `${secret.NAME}` for server-side API credentials. Header-row toggles and API method/format/authentication/HATEOAS toggles also accept optional variable references; boolean settings must resolve to `true` or `false`.
- Schemas: string, boolean, int64, decimal (precision/scale), double, date, UTC timestamp; nullable fields supported.
- CSV: UTF-8, configurable single-character delimiter, double-quoted fields, optional header. With headers, names/count must match the confirmed schema. Without headers, fields follow schema order.
- Invariant conversions. Date defaults to `yyyy-MM-dd`; timestamp defaults to .NET round-trip `O`. Date/timestamp mappings can specify a parse format. Blank numeric/boolean/date values become null only for nullable columns; strings preserve empty text.
- Filter comparisons use typed constants and ordinal string comparison. Ordinary comparisons involving null return false; explicit null checks are available.
- Invalid records fail the entire run. No skipped/rejected-row dataset is produced.
- PostgreSQL stores the latest pipeline revision, environments, workspace, immutable run snapshots, and run status. Saving uses optimistic revision checks.
- Snapshot creation and enqueueing are transactional. Snapshots contain resolved paths, selected environment variables, schemas and engine version. **Input files are not snapshotted.**
- File checks run after the attempt is recorded. An existing output, missing input, or invalid server path becomes a failed run in execution history, with its reason and block ID; files are never overwritten. Invalid definitions, missing variables, and stale revisions are still rejected before enqueueing.
- One long-running TickerQ job runs a pool that drains the durable PostgreSQL queue, with a fresh DI scope for each claim/execution. Configure `PipelineWorker:PoolSize` (environment variable `PipelineWorker__PoolSize`, default 2, range 1–64) to limit concurrent runs. Idle runners wait one second; busy runners claim the next run immediately. Runs can finish out of order and competing outputs are never overwritten. There is no automatic pipeline retry. Run only one API instance against a database: recovery marks all running executions interrupted on startup.
- Startup waits for the worker to start, and readiness requires the worker itself to be running, not just the scheduler. Duplicate dashboard invocations cannot start a second worker in the same process.
- CSV processing streams into 1,024-row Parquet batches. Each CSV field is limited to 1 MiB. Output is written to a unique temporary file, closed, and moved without overwrite on success. Cancellation/failure cleans up temporary output.
- Cancellation is cooperative; a request arriving after final publication may be too late. Counts are currently returned on successful completion, not live per step.
- Restarted processes mark formerly running jobs `Interrupted`; check files before rerunning. A crash between publishing a file and updating run status cannot be made atomic across PostgreSQL and the filesystem.

## Tests and build

```powershell
dotnet test backend/PipelineBuilder.slnx
dotnet build backend/PipelineBuilder.slnx -p:CI=true
cd frontend
npm test
npm run build
```

Database tests use Testcontainers and need Docker. Without Docker:

```powershell
dotnet test backend/PipelineBuilder.slnx --filter 'TestCategory!=Docker'
```

## TickerQ dashboard

In Development, open <http://localhost:5050/tickerq-dashboard>. This dashboard shows the TickerQ scheduler and its long-running `ExecutePipeline` worker. Individual pipeline attempts and their durable results remain on the application's execution logs page; they are not separate TickerQ jobs.

The dashboard has no authentication and is intentionally not registered in Production. Its CORS origin is limited to the local backend. Keep the development API local; the dashboard includes powerful scheduler controls. Stopping/cancelling the worker stops queue processing and makes readiness unhealthy; restart the API to resume it. TickerQ's own in-memory dashboard history resets with the process; pipeline history stays in PostgreSQL.

The legacy dashboard has its own numeric-enum JSON contract, isolated from the pipeline API's strict JSON settings. Its old transitive JSON/cryptography dependencies are pinned to patched releases rather than suppressing NuGet security checks.

## POC limits

- Local/trusted network only: no authentication or authorization. Do not expose this service publicly.
- One API/worker instance. Add leases before running multiple instances; startup interruption handling assumes one worker.
- Startup uses EF Core `EnsureCreated` plus idempotent additive upgrades (`IsDeleted`, run `Trigger`, schedule tables) for existing POC databases, preserving their records. Adopt EF migrations before further deployed schema changes.
- A scheduler registration is updated before its database transaction commits. If that commit then fails, the stale registration only fires a no-op because each trigger re-reads the persisted schedule; it is corrected on the next schedule change or restart.
- Saved pipeline definitions must be valid; incomplete drafts remain in the editor until fixed. Only the current saved revision can be started; existing runs retain their own snapshots.
- Discovery suggests a schema, not a guarantee about later records. Review it before execution.
- No branching, uploads, scheduled pipelines, plugins, calculated mapping fields, partitioning, append/overwrite, secret vault, or production telemetry/export configuration yet.
- TickerQ is pinned to dual-licensed MIT/Apache 2.0 version 2.5.3, which fixes the older in-memory scheduler's unsupported child-job lookup. Review licensing and compatibility before upgrading to current commercial/source-available releases.

Stop development servers with Ctrl+C in their terminals. `docker compose down` stops PostgreSQL without deleting its persisted volume.
