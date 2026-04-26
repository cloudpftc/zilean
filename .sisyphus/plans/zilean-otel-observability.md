# OpenTelemetry Observability for Zilean on SigNoz

## TL;DR

> **Quick Summary**: Add full OpenTelemetry observability (traces, metrics, structured logs, trace-log correlation) to the Zilean .NET 9 torrent indexer, exporting to a self-hosted SigNoz via gRPC/OTLP. Layer custom instrumentation on top of the existing SimCube.Aspire OTel baseline.
>
> **Deliverables**:
> - Custom `ActivitySource` and `Meter` for all Zilean business operations
> - Process metrics (CPU, memory) via `OpenTelemetry.Instrumentation.Process`
> - Manual `Activity` spans around ~15 service/method categories
> - Scraper CLI OTel bootstrap (separate service: `Zilean.Scraper`)
> - Docker compose network integration with SigNoz
> - Sampling configuration for production
> - Scraper short-lived process span flush
>
> **Estimated Effort**: Medium (15 tasks, 3 waves + verification)
> **Parallel Execution**: YES - 3 waves, 5-7 tasks per wave
> **Critical Path**: Task 1 → Task 5 → Task 8 → Task 12 → F1-F4

---

## Context

### Original Request
> "Full OpenTelemetry observability for Zilean .NET 9 app targeting self-hosted SigNoz — complete app-side instrumentation with traces, structured logs, metrics, trace-log correlation, manual spans around business logic, and Docker networking."

### Interview Summary
**Key Discussions**:
- SigNoz is already running on `signoz-net` network with collector at `signoz-otel-collector:4317` (gRPC)
- The app uses SimCube.Aspire 1.0.5 which provides baseline OTel auto-instrumentation (ASP.NET Core tracing, HttpClient tracing, runtime metrics, Serilog with OTel sink)
- SimCube.Aspire does NOT register custom ActivitySource or Meter — those must be added
- No `OpenTelemetry.Instrumentation.Process`, no sampling, no manual spans exist
- Scraper project does NOT call `AddOtlpServiceDefaults()` and uses `Host.CreateDefaultBuilder()` — needs manual OTel bootstrap
- Docker compose has empty `networks: {}` and no OTLP env vars
- ~25 services/methods need manual instrumentation

**Research Findings**:
- gRPC (port 4317) is the recommended protocol for .NET backend apps; requires `OTEL_EXPORTER_OTLP_INSECURE=true` for non-TLS
- Self-hosted SigNoz does NOT require `signoz-ingestion-key`
- `OpenTelemetry.Instrumentation.Process` is beta (1.15.1-beta.1)
- .NET 9 has built-in HTTP semantic conventions that can conflict with `OpenTelemetry.Instrumentation.Http` < 1.10.0 — SimCube.Aspire 1.0.5 may pull in 1.9.x
- `AddOtlpServiceDefaults()` likely already configures the OTLP exporter — must NOT duplicate
- Scraper must use `OpenTelemetry.Extensions.Hosting` pattern for generic `IHost`
- Short-lived CLI processes (scraper) must force-flush spans before exit
- Alpine Linux `/proc` filesystem may differ for process metrics

### Metis Review
**Identified Gaps** (addressed):
- Protocol correction (gRPC 4317, not HTTP 4318): Applied — changed env var target
- `OTEL_EXPORTER_OTLP_INSECURE` requirement: Applied — added as mandatory env var
- .NET 9 HttpClient double-tagging conflict: Applied — added version override check task
- Scraper service name separation (`Zilean.Scraper` vs `Zilean`): Applied — separate service
- Sampling configuration: Applied — ParentBased + TraceIdRatioBased (25%)
- Scraper span flush: Applied — ForceFlush before CLI exit
- Edge cases (collector down, high cardinality, Coravel cancellation): Applied — added to guardrails

---

## Work Objectives

### Core Objective
Add comprehensive OpenTelemetry observability to Zilean by layering custom instrumentation (ActivitySource, Meter, manual spans, process metrics, sampling) on top of the existing SimCube.Aspire baseline, with Docker compose integration to SigNoz.

### Concrete Deliverables
- `src/Zilean.Shared/Telemetry/ZileanTelemetry.cs` — static ActivitySource and Meter helper
- Updated `Directory.Packages.props` — OTel packages added
- Updated `ServiceCollectionExtensions.cs` — custom sources/meters registration
- Updated `docker-compose-test.yaml` — SigNoz network + OTLP env vars
- Manual `Activity` spans in ~15 service/method categories (ApiService + Scraper)
- Scraper OTel bootstrap in `src/Zilean.Scraper/Program.cs`

### Definition of Done
- [ ] `dotnet build src/Zilean.sln --no-restore` succeeds with 0 errors
- [ ] SigNoz Service Catalog shows "Zilean" service with traces
- [ ] SigNoz Service Catalog shows "Zilean.Scraper" service with traces
- [ ] Traces show correct hierarchy: root span → child spans for business operations
- [ ] Process metrics (CPU, memory) visible in SigNoz
- [ ] Scraper spans flushed and visible after CLI exit
- [ ] Zero OTel export errors in Docker logs
- [ ] Existing tests pass unchanged

### Must Have
- Custom `ActivitySource("Zilean")` and `Meter("Zilean")` registered
- Process instrumentation (`OpenTelemetry.Instrumentation.Process`)
- Sampling configured (ParentBased + TraceIdRatioBased, 25%)
- Manual spans around ALL business operations (database, API, jobs, HTTP, shell, file)
- Scraper OTel bootstrap with force-flush
- Docker compose connected to SigNoz network
- `OTEL_EXPORTER_OTLP_INSECURE=true` set

### Must NOT Have (Guardrails)
- Do NOT remove or replace SimCube.Aspire's `AddOtlpServiceDefaults()`
- Do NOT duplicate OTLP exporter configuration (inspect first)
- Do NOT alter business logic — spans must be transparent wrappers
- Do NOT migrate raw `HttpClient` to `IHttpClientFactory` (out of scope)
- Do NOT add EF Core instrumentation (out of scope for this phase)
- Do NOT build SigNoz dashboards or alerts (separate plan)
- Do NOT add per-SQL-query spans — span at service method level only
- Do NOT create .NET solution for individual projects — no csproj changes beyond packages
- Do NOT trigger a full dotnet restore that requires network — build with --no-restore
- Do NOT change SigNoz services (read-only access to signoz-net)
- Stringify all metric values properly in env vars (do NOT pass raw numbers for boolean-like metrics)
- Avoid high-cardinality span attributes — truncate query strings to 100 chars, no full torrent names

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: YES (xUnit + `bun test` wrappers in `tests/`)
- **Automated tests**: Tests-after (existing tests preserved; OTel adds no new unit tests; verification via SigNoz export)
- **Framework**: xUnit (.NET) + bun test (Node)

### QA Policy
Every task MUST include agent-executed QA scenarios (see TODO template below).
Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

- **API/Backend**: Use Bash (curl) — Send requests, assert status + response fields
- **Docker**: Use Bash — `docker exec`, `docker logs`, `docker inspect`
- **SigNoz verification**: Use Bash (curl) against SigNoz API for service catalog, trace counts

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately — foundation + scaffolding):
├── Task 1: Inspect SimCube.Aspire OTel configuration [unspecified-low]
├── Task 2: Add OTel packages to Directory.Packages.props [quick]
├── Task 3: Create ZileanTelemetry helper class [quick]
├── Task 4: ApiService — Register custom sources, meters, process, sampling [quick]
├── Task 5: Scraper — Manual OTel bootstrap [quick]
└── Task 6: Docker compose — SigNoz network + OTLP env vars [quick]

Wave 2 (After Wave 1 — manual instrumentation, MAX PARALLEL):
├── Task 7: Instrument Coravel jobs + hosted services [deep]
├── Task 8: Instrument database services [deep]
├── Task 9: Instrument API endpoints + audit/log services [deep]
├── Task 10: Instrument HTTP clients (Dmm/Imdb downloaders) [quick]
├── Task 11: Instrument file operations + shell/Python execution [quick]
└── Task 12: Instrument Scraper CLI commands [quick]

Wave FINAL (After ALL tasks — 4 parallel reviews):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real manual QA (unspecified-high)
└── Task F4: Scope fidelity check (deep)
→ Present results → Get explicit user okay
```

**Critical Path**: Task 1 → Task 5 (Scraper OTel bootstrap depends on knowing the pattern) → Task 8 (database services) → Task 12 (scraper commands) → F1-F4
**Parallel Speedup**: ~65% faster than sequential
**Max Concurrent**: 6 (Waves 1 & 2)

---

## TODOs

> Implementation + Verification = ONE Task. Never separate.

- [ ] 1. **Inspect SimCube.Aspire's `AddOtlpServiceDefaults()` OTel Configuration**

  **What to do**:
  - Read `AddOtlpServiceDefaults()` source from the SimCube.Aspire NuGet package (decompiled from `~/.nuget/packages/simcube.aspire/1.0.5/`)
  - Determine what it already configures: OTLP exporter? Serilog OTel sink? Resource builder? AddSource? AddMeter? Health checks?
  - Run `dotnet list package --include-transitive --framework net9.0` on `Zilean.ApiService.csproj` to find the exact version of `OpenTelemetry.Instrumentation.Http` pulled in
  - Document findings for use by Tasks 4, 5

  **Must NOT do**:
  - Do NOT modify SimCube.Aspire source or version
  - Do NOT make assumptions — verify by reading actual code

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low`
    - Reason: Research-only task — read and document, no code changes
  - **Skills**: [`file-inspect`, `file-search`]
    - `file-inspect`: Reading decompiled SimCube.Aspire source
    - `file-search`: Finding the NuGet cache path and package versions

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1, with Tasks 2, 3, 6)
  - **Parallel Group**: Wave 1
  - **Blocks**: Tasks 4, 5 (pattern needed to extend config)
  - **Blocked By**: None (can start immediately)

  **References** (CRITICAL):
  - **Pattern References**: `src/Zilean.ApiService/Program.cs:7` — `builder.AddOtlpServiceDefaults()` call site
  - **External References**: `~/.nuget/packages/simcube.aspire/1.0.5/` — SimCube.Aspire source (decompile with `dotnet tool run ilspy` or read raw)

  **Acceptance Criteria**:
  - [ ] Document lists exactly what `AddOtlpServiceDefaults()` configures (yes/no for: OTLP exporter, Serilog OTel sink, Resource builder, AddSource, AddMeter, AddAspNetCoreInstrumentation, AddHttpClientInstrumentation, health checks)
  - [ ] Document lists exact `OpenTelemetry.Instrumentation.Http` version from transitive dependencies
  - [ ] If Http instrumentation < 1.10.0, flag with `[VERSION OVERRIDE NEEDED]` note

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: Http instrumentation version check
    Tool: Bash
    Preconditions: .NET 9 SDK installed, packages restored
    Steps:
      1. cd src/Zilean.ApiService && dotnet list package --include-transitive --framework net9.0 2>&1 | tee /tmp/otel-transitive.txt
      2. rg "OpenTelemetry.Instrumentation.Http" /tmp/otel-transitive.txt
    Expected Result: Output shows the resolved version (e.g., "1.9.0" or "1.10.0")
    Failure Indicators: No output, command error, or version < 1.10.0
    Evidence: .sisyphus/evidence/task-1-version-check.txt
  ```

  **Evidence to Capture**:
  - [ ] Documentation of SimCube.Aspire OTel config written as inline findings
  - [ ] Version check output saved

  **Commit**: YES (groups with Task 2)
  - Message: `chore(otel): document SimCube.Aspire OTel configuration`
  - Files: TBD after research

- [ ] 2. **Add OTel Packages to Directory.Packages.props**

  **What to do**:
  - Add `OpenTelemetry.Instrumentation.Process` version `1.15.1-beta.1` to `Directory.Packages.props`
  - Add `OpenTelemetry.Extensions.Hosting` (latest stable) to `Directory.Packages.props`
  - If Task 1 found `OpenTelemetry.Instrumentation.Http` < 1.10.0, add version override `1.10.0` to `Directory.Packages.props`
  - Reference the Process package in `Zilean.ApiService.csproj` and `Zilean.Scraper.csproj` (NOT Shared — avoid transitive pull)
  - Reference the Hosting package in `Zilean.Scraper.csproj` only (ApiService uses SimCube.Aspire's host)

  **Must NOT do**:
  - Do NOT add packages to `Zilean.Shared.csproj` or `Zilean.Database.csproj`
  - Do NOT add EF Core instrumentation package
  - Do NOT run `dotnet restore` with network (use `--no-restore` for build)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Simple XML file edits, well-defined package versions
  - **Skills**: [`file-inspect`]
    - `file-inspect`: Reading/editing XML

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1, with Tasks 1, 3, 6)
  - **Parallel Group**: Wave 1
  - **Blocks**: Tasks 4, 5 (need packages to compile)
  - **Blocked By**: Task 1 findings (for version override decision)

  **References**:
  - **Pattern References**: `Directory.Packages.props` — existing `PackageVersion` entries show the format to follow
  - **External References**: NuGet.org for latest stable versions

  **Acceptance Criteria**:
  - [ ] `Directory.Packages.props` contains `OpenTelemetry.Instrumentation.Process` entry
  - [ ] `Directory.Packages.props` contains `OpenTelemetry.Extensions.Hosting` entry
  - [ ] If Task 1 flagged version conflict, `OpenTelemetry.Instrumentation.Http` override exists
  - [ ] `Zilean.ApiService.csproj` references Process package
  - [ ] `Zilean.Scraper.csproj` references both Process + Hosting packages

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: Packages added and build succeeds
    Tool: Bash
    Preconditions: Task 1 completed, packages not yet restored
    Steps:
      1. cat Directory.Packages.props | rg "Instrumentation.Process"
      2. cat src/Zilean.ApiService/Zilean.ApiService.csproj | rg "Instrumentation.Process"
      3. cat src/Zilean.Scraper/Zilean.Scraper.csproj | rg "Instrumentation.Process"
    Expected Result: All greps show matching entries
    Failure Indicators: Missing entries in any of the three files
    Evidence: .sisyphus/evidence/task-2-packages.txt
  ```

  **Evidence to Capture**:
  - [ ] Grep output from Directory.Packages.props and csproj files

  **Commit**: YES (groups with Task 1)
  - Message: `chore(otel): add OpenTelemetry packages for observability`
  - Files: `Directory.Packages.props`, `src/Zilean.ApiService/Zilean.ApiService.csproj`, `src/Zilean.Scraper/Zilean.Scraper.csproj`

- [ ] 3. **Create ZileanTelemetry Helper Class**

  **What to do**:
  - Create `src/Zilean.Shared/Telemetry/ZileanTelemetry.cs`
  - Define static `ActivitySource` named `"Zilean"` (version `"1.0.0"`)
  - Define static `Meter` named `"Zilean"` (version `"1.0.0"`)
  - Add helper method: `StartActivity(string name, ActivityKind kind = ActivityKind.Internal, IDictionary<string, object> tags = null)` that returns `Activity?` with tags set
  - Add helper method: `RecordException(Activity activity, Exception ex)` that sets span status to Error and records exception
  - Add helper constants for span status tags: `Ok`, `Error`
  - Add helper method: `SanitizeAttribute(string value, int maxLength = 100)` that truncates high-cardinality strings
  - Ensure all methods handle null gracefully (StartActivity returns null if no listener configured)

  **Must NOT do**:
  - Do NOT reference SimCube.Aspire types (Shared doesn't depend on it)
  - Do NOT register anything in DI — this is a static utility class
  - Do NOT log to ILogger — use `Activity?.AddEvent()` instead

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Single-file static utility class with well-defined API
  - **Skills**: [`file-inspect`]
    - `file-inspect`: Reading existing code patterns for style reference

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1, with Tasks 1, 2, 6)
  - **Parallel Group**: Wave 1
  - **Blocks**: Tasks 4, 5, 7–12 (all instrumentation depends on this helper)
  - **Blocked By**: None (can start immediately)

  **References**:
  - **Pattern References**: `src/Zilean.Shared/` — existing namespace and file organization conventions
  - **External References**: OpenTelemetry .NET docs: `ActivitySource` static pattern

  **Acceptance Criteria**:
  - [ ] File exists at `src/Zilean.Shared/Telemetry/ZileanTelemetry.cs`
  - [ ] Contains static `ActivitySource` with name `"Zilean"`
  - [ ] Contains static `Meter` with name `"Zilean"`
  - [ ] StartActivity, RecordException, SanitizeAttribute methods exist
  - [ ] All methods handle null Activity gracefully

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: File structure correct
    Tool: Bash
    Preconditions: File created
    Steps:
      1. cat src/Zilean.Shared/Telemetry/ZileanTelemetry.cs
      2. Verify: public static readonly ActivitySource Source = new("Zilean", "1.0.0")
      3. Verify: public static readonly Meter Meter = new("Zilean", "1.0.0")
      4. Verify: StartActivity(string, ActivityKind, IDictionary<string, object>?) method
      5. Verify: RecordException(Activity, Exception) method
    Expected Result: All five checks pass
    Failure Indicators: Missing any of the required members
    Evidence: .sisyphus/evidence/task-3-telemetry.txt

  Scenario: Compilation check (no reference to SimCube.Aspire types)
    Tool: Bash
    Preconditions: Task 2 packages in place
    Steps:
      1. rg "SimCube" src/Zilean.Shared/Telemetry/ZileanTelemetry.cs
    Expected Result: No matches (empty output)
    Failure Indicators: Any SimCube reference found
    Evidence: .sisyphus/evidence/task-3-no-simcube-ref.txt
  ```

  **Evidence to Capture**:
  - [ ] File contents
  - [ ] Grep for no SimCube references

  **Commit**: YES (groups with Task 2 if not already grouped)
  - Message: `feat(otel): add ZileanTelemetry helper with ActivitySource and Meter`
  - Files: `src/Zilean.Shared/Telemetry/ZileanTelemetry.cs`

- [ ] 4. **ApiService — Register Custom OTel Sources, Meters, Process Instrumentation, and Sampling**

  **What to do**:
  - In `src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs`, add a new extension method: `AddZileanTelemetry(this IServiceCollection services)`
  - Based on Task 1 findings, extend the existing OTel configuration (do NOT duplicate what SimCube.Aspire already sets up):
    - If SimCube.Aspire already calls `AddOtlpExporter()`, only register `.AddSource("Zilean")` and `.AddMeter("Zilean")` to the existing tracer/metrics builder
    - If SimCube.Aspire does NOT configure the exporter, add both `.AddSource()`, `.AddMeter()`, and `AddOtlpExporter()`
  - Add `OpenTelemetry.Instrumentation.Process` instrumentation to the meter provider
  - Configure sampling: `SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.25)))`
  - Configure OTLP export options: `TimeoutMilliseconds = 10000`, `ExportProcessorType = ExportProcessorType.Batch`
  - Add `ConfigureResource(r => r.AddService("Zilean"))` to set the service name (in case env var not set)
  - Call the new method from `Program.cs` after `builder.AddOtlpServiceDefaults()`
  - Ensure Serilog OTel sink is wired (if SimCube.Aspire doesn't already handle it)

  **Must NOT do**:
  - Do NOT remove `builder.AddOtlpServiceDefaults()` call
  - Do NOT duplicate OTLP exporter configuration
  - Do NOT hardcode the OTLP endpoint — use env vars only

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Must carefully integrate with SimCube.Aspire to avoid duplication; requires understanding of Task 1 findings
  - **Skills**: [`file-inspect`, `file-search`]
    - `file-inspect`: Reading existing OTel config patterns
    - `file-search`: Finding OTel-related code in the project

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 1, 2, 3)
  - **Parallel Group**: Wave 1 (last to run in this wave)
  - **Blocks**: Tasks 7–11 (instrumentation needs sources registered)
  - **Blocked By**: Task 1 (inspect config), Task 2 (packages), Task 3 (helper)

  **References**:
  - **Pattern References**: `src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs` — existing extension method pattern
  - **Pattern References**: `src/Zilean.ApiService/Program.cs:7` — `builder.AddOtlpServiceDefaults()` call site
  - **External References**: OpenTelemetry .NET docs for `AddOtlpExporter`, `SetSampler`, `AddInstrumentation`

  **Acceptance Criteria**:
  - [ ] `AddZileanTelemetry()` extension method exists in `ServiceCollectionExtensions.cs`
  - [ ] Method is called from `Program.cs` after `AddOtlpServiceDefaults()`
  - [ ] `.AddSource("Zilean")` registered
  - [ ] `.AddMeter("Zilean")` registered
  - [ ] Process instrumentation added
  - [ ] Sampling configured (ParentBased + 25%)
  - [ ] No duplicate exporter configuration

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: Configuration compiles and wires correctly
    Tool: Bash
    Preconditions: Tasks 1-4 complete, dotnet build passes
    Steps:
      1. cat src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs | rg "AddZileanTelemetry"
      2. cat src/Zilean.ApiService/Program.cs | rg "AddZileanTelemetry"
      3. cat src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs | rg "AddSource"
      4. cat src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs | rg "AddMeter"
      5. cat src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs | rg "Instrumentation.Process"
      6. cat src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs | rg "Sampler"
    Expected Result: All six grep commands return matching lines
    Failure Indicators: Any grep returns no output
    Evidence: .sisyphus/evidence/task-4-config.txt
  ```

  **Evidence to Capture**:
  - [ ] Grep output confirming all registrations

  **Commit**: YES
  - Message: `feat(otel): register custom OTel sources, process metrics, and sampling for ApiService`
  - Files: `src/Zilean.ApiService/Features/Bootstrapping/ServiceCollectionExtensions.cs`, `src/Zilean.ApiService/Program.cs`

- [ ] 5. **Scraper — Manual OTel Bootstrap**

  **What to do**:
  - In `src/Zilean.Scraper/Program.cs`:
    - Remove any existing Serilog configuration that conflicts with OTel
    - Add OTel tracing setup using `OpenTelemetry.Extensions.Hosting`:
      - `Sdk.CreateTracerProviderBuilder()` with `.AddSource("Zilean")`, `.AddOtlpExporter()`, `.SetSampler(...)`
    - Add OTel metrics setup:
      - `Sdk.CreateMeterProviderBuilder()` with `.AddMeter("Zilean")`, `.AddInstrumentation<ProcessInstrumentation>()`, `.AddOtlpExporter()`
    - Configure `OTEL_SERVICE_NAME=Zilean.Scraper` (separate from ApiService)
    - Add `AppDomain.CurrentDomain.ProcessExit += (_, _) => tracerProvider?.ForceFlush(5000)` for short-lived process span flush
    - Ensure all OTel configuration reads from env vars (OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_EXPORTER_OTLP_PROTOCOL, OTEL_EXPORTER_OTLP_INSECURE)
  - Reuse `ZileanTelemetry` helper from Shared (already referenced)

  **Must NOT do**:
  - Do NOT call `builder.AddOtlpServiceDefaults()` (Scraper doesn't use Aspire)
  - Do NOT use `WebApplicationBuilder` methods (Scraper uses `Host.CreateDefaultBuilder()`)
  - Do NOT configure Serilog OTel sink manually if SimCube.Aspire is referenced transitively (check Task 1)

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Manual OTel bootstrap requires understanding generic IHost pattern; force-flush for short-lived CLI
  - **Skills**: [`file-inspect`, `file-search`]
    - `file-inspect`: Reading Scraper Program.cs current state
    - `file-search`: Finding Serilog config patterns

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 2, 3; runs after Task 4 in Wave 1)
  - **Parallel Group**: Wave 1 (last to run in this wave)
  - **Blocks**: Task 12 (scraper CLI instrumentation)
  - **Blocked By**: Task 2 (packages), Task 3 (helper)

  **References**:
  - **Pattern References**: `src/Zilean.Scraper/Program.cs` — current `Host.CreateDefaultBuilder()` setup
  - **External References**: OpenTelemetry .NET docs for generic IHost, `Sdk.CreateTracerProviderBuilder()`

  **Acceptance Criteria**:
  - [ ] Scraper Program.cs imports OTel namespaces
  - [ ] TracerProvider builder configured with `.AddSource("Zilean")`
  - [ ] MeterProvider builder configured with `.AddMeter("Zilean")` and Process instrumentation
  - [ ] `OTEL_SERVICE_NAME=Zilean.Scraper` set in env or resource builder
  - [ ] Process exit handler calls `ForceFlush(5000)`
  - [ ] No conflicting Serilog manual config

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: Scraper OTel config compiles
    Tool: Bash
    Preconditions: Tasks 2, 3, 5 complete
    Steps:
      1. rg "TracerProvider" src/Zilean.Scraper/Program.cs
      2. rg "MeterProvider" src/Zilean.Scraper/Program.cs
      3. rg "ForceFlush" src/Zilean.Scraper/Program.cs
      4. rg "OTEL_SERVICE_NAME|Zilean.Scraper" src/Zilean.Scraper/Program.cs
    Expected Result: All four grep commands return matching lines
    Failure Indicators: Any grep returns no output
    Evidence: .sisyphus/evidence/task-5-scraper-config.txt
  ```

  **Evidence to Capture**:
  - [ ] Grep output confirming all registrations

  **Commit**: YES
  - Message: `feat(otel): add manual OTel bootstrap for Scraper with force-flush`
  - Files: `src/Zilean.Scraper/Program.cs`

- [ ] 6. **Docker Compose — SigNoz Network Integration and OTLP Env Vars**

  **What to do**:
  - Replace `networks: {}` (empty) with proper network declarations:
    - Default network for postgres + comet-zilean (internal)
    - External `signoz-net` network declaration
  - Attach ONLY the `zilean` service to `signoz-net`
  - Add OTLP env vars to `zilean` service:
    - `OTEL_EXPORTER_OTLP_ENDPOINT=http://signoz-otel-collector:4317`
    - `OTEL_EXPORTER_OTLP_PROTOCOL=grpc`
    - `OTEL_EXPORTER_OTLP_INSECURE=true`
    - `OTEL_SERVICE_NAME=Zilean`
    - `OTEL_RESOURCE_ATTRIBUTES=service.instance.id=zilean-test,deployment.environment=test`
    - `OTEL_DOTNET_AUTO_METRICS_PLUGIN_ENABLED=true` (enable process metrics)
    - `OTEL_EXPORTER_OTLP_TIMEOUT=10000` (10s export timeout)
  - Verify `signoz-net` exists: `docker network ls | grep signoz-net`

  **Must NOT do**:
  - Do NOT attach postgres or comet-zilean to signoz-net
  - Do NOT change SigNoz services
  - Do NOT hardcode the collector IP — use service name

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: YAML file edit with well-defined env vars
  - **Skills**: [`file-inspect`]
    - `file-inspect`: Reading/writing YAML

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1, with Tasks 1, 2, 3)
  - **Parallel Group**: Wave 1
  - **Blocks**: Task F3 (QA verification needs docker network)
  - **Blocked By**: None (can start immediately)

  **References**:
  - **Pattern References**: `docker-compose-test.yaml` — existing compose file with empty `networks: {}`
  - **External References**: Docker Compose external network docs

  **Acceptance Criteria**:
  - [ ] `networks` section declares `signoz-net: { external: true }` and default network
  - [ ] `zilean` service has `networks: [default, signoz-net]`
  - [ ] All 7 OTLP env vars present in `zilean` service
  - [ ] postgres and comet-zilean do NOT have `signoz-net` in networks

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: Docker compose validates
    Tool: Bash
    Preconditions: docker compose installed
    Steps:
      1. docker compose -f docker-compose-test.yaml config 2>&1 | jq '.services.zilean.environment' -c
      2. Verify OTEL_EXPORTER_OTLP_ENDPOINT present
      3. Verify OTEL_EXPORTER_OTLP_INSECURE=true
      4. docker compose -f docker-compose-test.yaml config 2>&1 | jq '.services.zilean.networks'
      5. Verify signoz-net is in the list
    Expected Result: All env vars present, networks include signoz-net
    Failure Indicators: Missing env vars, missing network, compose validation error
    Evidence: .sisyphus/evidence/task-6-docker-compose.txt
  ```

  **Evidence to Capture**:
  - [ ] Docker compose config output with jq extracts
  - [ ] `docker network ls` output showing signoz-net exists

  **Commit**: YES
  - Message: `feat(otel): connect Zilean to SigNoz network with OTLP env vars`
  - Files: `docker-compose-test.yaml`

- [ ] 7. **Instrument Coravel Jobs + Hosted Services**

  **What to do**:
  - Add `using var span = ZileanTelemetry.Source.StartActivity("Schedule.JobName")` in `ExecuteAsync()` of:
    - `DmmSyncJob.cs` → `"Schedule.DmmSync"`
    - `GenericSyncJob.cs` → `"Schedule.GenericSync"` (add category attribute)
    - `BackgroundRefreshJob.cs` → `"Schedule.BackgroundRefresh"`
  - In hosted services (`EnsureMigrated.cs`, `ConfigurationUpdaterService.cs`):
    - `"Hosted.EnsureMigrated"`, `"Hosted.ConfigurationRefresh"`
  - Wrap in try/catch for `OperationCanceledException` → span status `Unset`
  - For other exceptions: `ZileanTelemetry.RecordException(span, ex)`, status to `Error`
  - Tags: `"scheduler.type"="coravel"`, `"job.name"`=class name

  **Must NOT do**: Change `IInvocable` interface. No custom Coravel base class.

  **Agent**: `deep` | **Skills**: file-inspect, file-search | **Parallel**: Wave 2, with 8-12

  **References**: `src/Zilean.ApiService/Jobs/`, `src/Zilean.ApiService/Features/`, `ZileanTelemetry.cs`.

  **Acceptance Criteria**:
  - [ ] All 5 job/hosted service files instrumented
  - [ ] Cancellation → `Unset`, errors → `RecordException`
  - [ ] Tags: `scheduler.type=coravel`, `job.name`

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: Job spans exist with correct patterns
    Tool: Bash
    Steps:
      1. rg "StartActivity.*(Schedule|Hosted)\." src/Zilean.ApiService/ -n
      2. rg "RecordException" src/Zilean.ApiService/Jobs/ -n
    Expected: All 5 files show matches. Span names match Schedule.* or Hosted.*
    Evidence: .sisyphus/evidence/task-7-coravel-spans.txt
  ```

  **Commit**: YES. `feat(otel): add manual spans to Coravel jobs and hosted services`

- [ ] 8. **Instrument Database Services**

  **What to do**:
  - Add spans to key methods in Dapper services (method level, NOT per-query):
    - `TorrentInfoService.cs` — SearchAsync, GetByInfoHashAsync, Insert/Delete/Count/CheckCached
    - `ImdbFileService.cs` — SearchImdbAsync, GetImdbEntryByIdAsync
    - `DmmService.cs` — SearchDmmAsync, Get/Delete by infoHash
    - `ImdbPostgresMatchingService.cs` — MatchTorrentsAsync
    - `ImdbFuzzyStringMatchingService.cs` — MatchFuzzyTitlesAsync
  - Span naming: `"DB.{Service}.{Method}"` (e.g., `"DB.TorrentInfo.Search"`)
  - Attributes: `"db.system"="postgresql"`, `"db.operation"`=SELECT/INSERT/DELETE
  - Sanitize query params to ≤100 chars via `ZileanTelemetry.SanitizeAttribute()`

  **Must NOT do**: Dapper wrapper or per-SQL-query spans.

  **Agent**: `deep` | **Skills**: file-inspect, file-search | **Parallel**: Wave 2, with 7, 9-12

  **References**: `src/Zilean.Database/` — service files. `ZileanTelemetry.cs`.

  **Acceptance Criteria**:
  - [ ] 5 service files instrumented (method-level spans)
  - [ ] `db.system=postgresql` on all spans
  - [ ] Query params sanitized ≤100 chars

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: Database spans follow naming convention
    Tool: Bash
    Steps:
      1. rg "StartActivity.*DB\." src/Zilean.Database/ -n
      2. Verify: "DB.TorrentInfo", "DB.ImdbFile", "DB.Dmm", "DB.ImdbMatching"
    Expected: All 4 service prefixes found
    Evidence: .sisyphus/evidence/task-8-db-spans.txt
  ```

  **Commit**: YES. `feat(otel): add manual spans to database services`

- [ ] 9. **Instrument API Endpoints + Audit/Cache/Ingestion Services**

  **What to do**:
  - Endpoint files: `SearchEndpoints.cs` → `"API.Search"`, `TorznabEndpoints.cs` → `"API.Torznab"`, `TorrentsEndpoints.cs` → `"API.Torrents"`
  - Audit: `QueryAuditService.cs` → `"Audit.RecordQuery"`, `"Audit.GetRecentQueries"`, `"Audit.GetTopQueries"`. `FileAuditLogService.cs` → `"Audit.*"`.
  - Cache: `QueryCacheService.cs` → `"Cache.Get"`, `"Cache.Set"`, `"Cache.Invalidate"` with `"cache.hit"=true/false`.
  - Ingestion: `IngestionQueueService.cs` → `"Ingestion.*"`, `IngestionCheckpointService.cs` → `"Ingestion.*"`.
  - `MissTrackingService.cs` → `"Miss.Track"`, `"Miss.GetTop"`.
  - Tags: `"endpoint.type"` for API, `"audit.operation"` for audit.

  **Must NOT do**: Change route definitions or middleware.

  **Agent**: `deep` | **Skills**: file-inspect, file-search | **Parallel**: Wave 2, with 7, 8, 10-12

  **References**: `src/Zilean.ApiService/Endpoints/`, `src/Zilean.ApiService/Features/`.

  **Acceptance Criteria**:
  - [ ] 3 endpoint files + 7 service files instrumented
  - [ ] Cache spans tagged with `cache.hit`

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: API and service spans across all categories
    Tool: Bash
    Steps:
      1. rg "StartActivity.*API\." src/Zilean.ApiService/Endpoints/ -n
      2. rg "StartActivity.*(Audit|Cache|Ingestion|Miss)\." src/Zilean.ApiService/Features/ -n
    Expected: All directories show matches
    Evidence: .sisyphus/evidence/task-9-api-spans.txt
  ```

  **Commit**: YES. `feat(otel): add manual spans to API endpoints, audit, cache, and ingestion services`

- [ ] 10. **Instrument HTTP Clients (Dmm/Imdb Downloaders)**

  **What to do**:
  - Manual spans around raw `new HttpClient()` calls in:
    - `DmmFileDownloader.cs` → `"HTTP.DmmDownload"`, tags: `"http.url"` (sanitized), `"http.method"=GET`
    - `ImdbFileDownloader.cs` → `"HTTP.ImdbDownload"`, tags: `"http.url"` (sanitized)
    - `GenericIngestionScraping.cs` → `"HTTP.GenericScrape"`
    - `StreamedEntryProcessor.cs` → `"HTTP.StreamedProcess"`
  - Capture response status code + content length as span attributes
  - Set status to Error on non-success, record exceptions

  **Must NOT do**: Migrate to `IHttpClientFactory` (out of scope guardrail).

  **Agent**: `quick` | **Skills**: file-inspect | **Parallel**: Wave 2, with 7-9, 11, 12

  **References**: `src/Zilean.ApiService/Features/Scraping/`, `ZileanTelemetry.cs`.

  **Acceptance Criteria**:
  - [ ] All 4 HTTP client files instrumented
  - [ ] Response status + content length captured

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: HTTP client spans present
    Tool: Bash
    Steps:
      1. rg "StartActivity.*HTTP\." src/ -n
    Expected: Matches in all 4 downloader/scraper files
    Evidence: .sisyphus/evidence/task-10-http-spans.txt
  ```

  **Commit**: YES. `feat(otel): add manual spans to HTTP client operations`

- [ ] 11. **Instrument File Operations + Shell/Python Execution**

  **What to do**:
  - File ops: `ConfigurationUpdaterService.cs` → `"File.LoadConfiguration"`, `"File.UpdateConfiguration"`.
    `DmmScraping.cs` → `"File.ParseDmmData"`. `ZileanConfiguration.cs` → `"File.ReadConfig"`.
  - Shell: `ShellExecutionService.cs` → `"Shell.Execute"`, tags: `"shell.command"` (sanitized), `"shell.exit_code"`.
  - `ParseTorrentNameService.cs` → `"Shell.ParseTorrentName"`, sanitize torrent name to 100 chars.
  - Capture operation duration and success/failure as tags.

  **Must NOT do**: Python-side tracing. Full torrent names in attributes.

  **Agent**: `quick` | **Skills**: file-inspect | **Parallel**: Wave 2, with 7-10, 12

  **References**: `src/Zilean.Shared/Services/`, `ZileanTelemetry.cs`.

  **Acceptance Criteria**:
  - [ ] File/config services instrumented
  - [ ] Shell execution spans with command + exit code
  - [ ] Torrent name attributes ≤100 chars

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: File and shell spans exist
    Tool: Bash
    Steps:
      1. rg "StartActivity.*File\." src/ -n
      2. rg "StartActivity.*Shell\." src/ -n
    Expected: Multiple matches in both categories
    Evidence: .sisyphus/evidence/task-11-file-shell-spans.txt
  ```

  **Commit**: YES. `feat(otel): add manual spans to file operations and shell execution`

- [ ] 12. **Instrument Scraper CLI Commands**

  **What to do**:
  - Add root spans around scraper command handlers:
    - `DmmSyncCommand` → `"CLI.DmmSync"`, tag: `"cli.command=dmm-sync"`
    - `GenericSyncCommand` → `"CLI.GenericSync"`, tag: `"cli.command=generic-sync"`
    - `ResyncImdbCommand` → `"CLI.ResyncImdb"`, tag: `"cli.command=resync-imdb"`
  - Ensure spans started before work, disposed after
  - Short-lived process spans flushed by Task 5's `ForceFlush(5000)` at exit
  - Call `ZileanTelemetry.Source.ForEach(s => s.Dispose())` to trigger immediate export

  **Must NOT do**: Change Spectre.Console.Cli wiring.

  **Agent**: `quick` | **Skills**: file-inspect | **Parallel**: Wave 2, with 7-11

  **References**: `src/Zilean.Scraper/Commands/`, `ZileanTelemetry.cs`.

  **Acceptance Criteria**:
  - [ ] All 3 scraper commands have root spans
  - [ ] `cli.command` tag set on each
  - [ ] Force-flush/dispose called before exit

  **QA Scenarios (MANDATORY)**:
  ```
  Scenario: CLI spans exist
    Tool: Bash
    Steps:
      1. rg "StartActivity.*CLI\." src/Zilean.Scraper/ -n
    Expected: At least 3 matches (DmmSync, GenericSync, ResyncImdb)
    Evidence: .sisyphus/evidence/task-12-cli-spans.txt
  ```

  **Commit**: YES. `feat(otel): add manual spans to Scraper CLI commands with force-flush`

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present results to user and get explicit "okay".

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, grep for span patterns, check env vars in docker-compose). For each "Must NOT Have": search codebase for forbidden patterns — reject with file:line if found. Check evidence files exist in `.sisyphus/evidence/`.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [12/12] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build src/Zilean.sln --no-restore`. Review all changed files for: unused imports, nested try/catch that hides errors, spans without `using` disposal, missing null checks on `StartActivity()`, high-cardinality attributes (full torrent names, unsanitized URLs). Check guardrail compliance.
  Output: `Build [PASS/FAIL] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Start from clean state. Execute QA scenarios from all 12 tasks — follow exact grep steps. Verify docker compose validates. Verify cross-task integration: ZileanTelemetry helper is used in all instrumentation tasks, env vars match between docker-compose and code expectations.
  Output: `Scenarios [12/12 pass] | Integration [N/N] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff (git diff). Verify 1:1 — everything in spec was built, nothing beyond spec was built. Check "Must NOT do" compliance. Detect cross-task contamination: Task 10 touching Task 6's docker-compose file. Flag unaccounted changes.
  Output: `Tasks [12/12 compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

| Task(s) | Message | Files |
|---------|---------|-------|
| T1+T2 | `chore(otel): add OpenTelemetry packages and document SimCube.Aspire config` | `Directory.Packages.props`, `*.csproj` |
| T3 | `feat(otel): add ZileanTelemetry helper with ActivitySource and Meter` | `src/Zilean.Shared/Telemetry/ZileanTelemetry.cs` |
| T4 | `feat(otel): register custom OTel sources, process metrics, and sampling for ApiService` | `ServiceCollectionExtensions.cs`, `Program.cs` |
| T5 | `feat(otel): add manual OTel bootstrap for Scraper with force-flush` | `src/Zilean.Scraper/Program.cs` |
| T6 | `feat(otel): connect Zilean to SigNoz network with OTLP env vars` | `docker-compose-test.yaml` |
| T7 | `feat(otel): add manual spans to Coravel jobs and hosted services` | 5 job/service files |
| T8 | `feat(otel): add manual spans to database services` | 5-6 database service files |
| T9 | `feat(otel): add manual spans to API endpoints, audit, cache, and ingestion` | 10+ endpoint/service files |
| T10 | `feat(otel): add manual spans to HTTP client operations` | 4 HTTP client files |
| T11 | `feat(otel): add manual spans to file operations and shell execution` | ~8 file/shell service files |
| T12 | `feat(otel): add manual spans to Scraper CLI commands` | 3 scraper command files |

---

## Success Criteria

### Verification Commands
```bash
# Build check
dotnet build src/Zilean.sln --no-restore
# Expected: 0 errors, 0 warnings

# All manual spans present (count approximate)
rg "StartActivity" src/ --count
# Expected: 30+ matches across all service files

# Docker compose validates
docker compose -f docker-compose-test.yaml config
# Expected: no errors, networks section shows signoz-net

# Evidence files exist
ls .sisyphus/evidence/task-*
# Expected: 12+ evidence files
```

### Final Checklist
- [ ] All "Must Have" items present (ActivitySource, Meter, Process metrics, Sampling, Docker network, ForceFlush)
- [ ] All "Must NOT Have" items absent (no SimCube removal, no duplicate exporter, no IHttpClientFactory migration, no EF Core instrumentation)
- [ ] `dotnet build src/Zilean.sln --no-restore` passes
- [ ] 12 evidence files exist in `.sisyphus/evidence/`
- [ ] All 8 Metis acceptance criteria verifiable (see Context section for AC1-AC8)
- [ ] Final Verification Wave: all 4 reviews APPROVE

