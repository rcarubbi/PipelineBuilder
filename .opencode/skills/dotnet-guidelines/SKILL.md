---
name: .NET Architecture Guidelines
description: Apply the user's .NET architecture, implementation, infrastructure, and testing conventions when designing or changing .NET solutions.
---

# .NET Architecture Guidelines

Follow these rules for all .NET solutions.

## Design principles

Apply these principles pragmatically; prefer the smallest design that keeps domain behavior clear and dependencies understandable.

1. **High cohesion:** keep each type and method focused on a closely related responsibility.
2. **Low coupling:** depend on the smallest useful contract; pass collaborators only where they are needed.
3. **Depend on abstractions:** use interfaces or delegates at meaningful boundaries, not an interface for every class by default.
4. **Composition over inheritance:** compose behavior; use inheritance for genuine substitutability, not just code reuse.
5. **Separate creation from use:** construct and wire services in the composition root; consumers receive their dependencies.
6. **Start with the data:** define domain concepts, invariants, and boundary contracts before arranging layers and methods around them.
7. **Keep it simple:** use DRY to remove meaningful duplication, not to create generic frameworks; favor KISS and YAGNI.

## Design rules

Treat these as review prompts, not mechanical bans. Follow local conventions and avoid adding abstraction without a concrete benefit.

1. **Avoid type abuse.** Prefer explicit domain types over `object`, `dynamic`, unchecked casts, or strings used as hidden protocols. Do not wrap primitives unless the concept has real meaning, validation, or behavior.
2. **Avoid vague identifiers.** Name types, members, variables, and parameters for their role; use conventional abbreviations only when they improve clarity.
3. **Avoid Boolean switch parameters.** A Boolean that selects materially different behavior obscures a call site; prefer separate named operations, a strategy, or a small options type when needed. A Boolean that is itself the data is fine.
4. **Avoid deep nesting.** Use guard clauses and focused helpers to make control flow readable; do not extract trivial code just to reduce indentation.
5. **Tell, don't ask.** Keep domain decisions with the behavior and data they govern instead of exposing state for callers to inspect and mutate. Plain DTOs and boundary models may remain data-only.
6. **Avoid parallel data structures.** Keep related values together in a type or keyed collection instead of maintaining synchronized lists whose positions must stay aligned.
7. **Avoid god classes.** Keep classes cohesive; split responsibilities when the split clarifies ownership or change boundaries.
8. **Avoid feature envy.** Put domain behavior with the domain concept it uses, while keeping orchestration in its proper layer and infrastructure operations in Infrastructure.
9. **Avoid primitive obsession.** Use value objects or enums for domain concepts that need invariants or behavior; retain primitives at serialization, persistence, and simple-value boundaries where wrappers add no value.
10. **Remove dead code.** Delete unused members, imports, and obsolete paths when callers and compatibility requirements have been checked; do not keep speculative code for later.
11. **Avoid magic numbers.** Name values whose meaning is not obvious, especially domain limits and timing values; obvious syntax-level values do not need ceremony.
12. **Catch exceptions narrowly.** Catch only exceptions the code can handle or enrich meaningfully; never silently swallow failures, and preserve cancellation by not treating `OperationCanceledException` as an ordinary error.
13. **Dispose resources deterministically.** Use `using`/`await using` for owned `IDisposable`/`IAsyncDisposable` resources and keep their lifetime within the operation that uses them.
14. **Avoid shared mutable defaults.** Do not expose mutable static collections or shared mutable state as defaults; create per-instance state or use immutable collections where sharing is intended.
15. **Prefer clear imports.** Avoid broad or obscure global `using` directives and aliases that hide dependencies; SDK implicit usings and deliberate project-wide imports are fine.
16. **Use specific package and namespace names.** Names should communicate the library's purpose and avoid generic or collision-prone names.
17. **Mirror architecture in folders and tests.** Keep source folders aligned with the solution's layers and test structure, consistent with the project conventions below.
18. **Avoid type-check chains for behavior dispatch.** Prefer polymorphism or a strategy when variants own different behavior; use a switch expression for a small, closed set of values. Type checks remain appropriate for validation and boundary handling.
19. **Avoid overloaded classes and APIs.** Do not let a type accumulate unrelated responsibilities or a confusing overload set; use clear named operations and focused types when that improves the contract.
20. **Keep naming symmetric.** Use consistent paired names for related operations and concepts, such as `Read`/`Write`, `Start`/`End`, and `Create`/`Delete`.
21. **Make method names truthful.** Names should reflect observable behavior, side effects, async work, and whether a result can be absent; avoid names that promise more than the method does.
22. **Avoid hardwired initialization sequences.** Do not make consumers construct or start concrete collaborators in a hidden order. Wire dependencies in the composition root and express required startup ordering explicitly through the host or a startup initializer.

## Core principles and projects

- Clean Architecture is mandatory. Split each solution into four projects: `Presentation` (controllers, middleware, filters, Swagger, API versioning, ProblemDetails; no Minimal API), `Application` (UseCases, interfaces, DTOs, mapping, validation), `Domain` (domain logic), and `Infrastructure` (persistence, external services, file I/O, identity, caching). Apply Domain-Driven Design (DDD) patterns—such as aggregates, value objects, and domain events—only when the solution's size and complexity justify them; do not impose DDD ceremony on simpler solutions.
- Apply SOLID, DRY, KISS, and GoF patterns when they fit; prefer composition and do not use patterns for their own sake.
- Dependency direction is inward only: Presentation → Application → Domain; Infrastructure → Application → Domain. Domain references nothing; Application references Domain only; Infrastructure implements Application interfaces.
- Each layer owns a static DI registration extension. Compose registrations in Presentation startup; do not leak DI setup across layers.
- Presentation references Application and Infrastructure only for DI composition; it must not reference Domain or Infrastructure directly for application logic.

## DTO boundaries

- DTOs do not cross layer boundaries without mapping. Application uses UseCase InputRecords/OutputRecords; Presentation has request/response models; Infrastructure keeps internal DTOs private.
- Use explicit mapping only (`MapTo()` extensions or operators). Do not use AutoMapper, Mapster, or other mapping libraries.

## UseCases

- Place every UseCase in `Application/UseCases/`, named by domain and action (`CreateOrderCommand`, `GetOrderByIdQuery`), one file and one handler per UseCase.
- Every UseCase accepts an InputRecord and returns an OutputRecord (or `ErrorOr<OutputRecord>`); OutputRecords need no marker. InputRecords implement `IInput`.
- Use `IUseCase<in TIn, TOut> where TIn : IInput` with `Task<TOut> Handle(TIn input, CancellationToken ct)`.
- Queries are side-effect-free, return an OutputRecord, and read through a projection query class—not repositories. Commands write, use repositories for aggregate persistence, and are atomic within a DB transaction/unit-of-work commit.
- A class that only orchestrates domain entities through Application interfaces belongs in Application as a UseCase. Infrastructure contains actual implementations and code requiring infrastructure-specific APIs (EF DbContext, HttpClient, filesystem, Redis, Kafka, SMTP, etc.). If it can use only Application/Domain and Microsoft.Extensions abstractions, put it in Application. TickerQ job handlers and startup initializers are the stated exception: keep them in `Infrastructure/JobHandlers/`, delegating business logic to Application UseCases.

## Results, errors, and cross-cutting behavior

- Use `ErrorOr<T>` for domain/business results, not exceptions. Reserve exceptions for infrastructure/system failures. Common errors: Validation, NotFound, Conflict, Failure.
- UseCases return `ErrorOr<TOut>`; controllers map success/errors to HTTP responses/ProblemDetails. Never expose raw exceptions to controllers; middleware maps exceptions to ProblemDetails (RFC 7807).
- No mediator. Use Scrutor decorators for cross-cutting behavior: logging, exception handling, validation, transaction/unit-of-work, and performance tracking. Register decorators in `AddApplication()`.
- FluentValidation validators are per InputRecord and registered in `AddApplication()`; validation decorator runs before the handler.
- `IUnitOfWork` belongs in `Application/Interfaces/`; implementation in `Infrastructure/Persistence/`. Transactions wrap Commands only; commit on success, roll back on exception. Queries are read-only and do not require transactions.

## Logging and telemetry

- Use Serilog; Azure Monitor sink in production.
- Do not use inline `ILogger` extension calls/message templates in handler bodies. Use `[LoggerMessage]` compile-time source-generated methods in a private nested `static partial class Log` inside the consuming class. The outer class is `partial`; generated methods are `static partial`, accept `ILogger` explicitly as the first argument (not extension syntax), and are called as `Log.Method(_logger, ...)`. Drop the `Log` prefix in method names; one nested Log class per consumer; no separate `*Logging.cs` files.
- Use OpenTelemetry for distributed tracing, metrics, and log export; export to Azure Monitor using `Azure.Monitor.OpenTelemetry.Exporter`; propagate W3C TraceContext. Register in `AddInfrastructure()`. Let instrumentation propagate Activity; do not manually create/propagate it. Instrument ASP.NET Core, HttpClient, and EF Core as appropriate.

## Persistence and domain events

- Use EF Core + PostgreSQL via Npgsql as primary ORM; Dapper for high-performance read-only queries. Register DbContext in `AddInfrastructure()`.
- Repositories persist Aggregate Roots on the command side only. Define `IRepository<TAggregate>` in Application; implement in `Infrastructure/Persistence/Repositories/`.
- Queries bypass repositories and read via DbContext or Dapper in dedicated query/projection classes in `Application/Queries/` or beside the UseCase. Do not create read repository abstractions.
- Define `IDomainEventDispatcher` in `Domain/Interfaces/`. AggregateRoot holds raised events and exposes read-only events, `Raise`, and `ClearEvents`. After successful persistence, the command handler dispatches events; never dispatch inside the aggregate. Infrastructure implements dispatch via internal event bus/message queue.

## Resilience, rate limiting, API clients, and decorators

- Use `Microsoft.Extensions.Resilience` for non-HTTP infrastructure; use `Microsoft.Extensions.Resilience.Http` and `AddStandardResilienceHandler()` for HTTP clients. Configure per client/handler in `AddInfrastructure()`, customize defaults only as needed. Every infrastructure component must have a resilience pipeline; never use `DelegatingHandler` for retry.
- Server rate limiting uses `System.Threading.RateLimiting`; apply via endpoint `EnableRateLimiting` or conventions. Use fixed window, token bucket for bursts, or sliding window for steady traffic. Register in `AddInfrastructure()`; no custom controller rate-limit logic.
- Inject HttpClient through `IHttpClientFactory` or use Refit for typed APIs. Put authentication/header/correlation ID injection in DelegatingHandlers; retries belong to resilience.
- API client options are `{ClientName}Options` with matching section name. Bind all options with `AddOptions<T>().BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart()`; never `services.Configure<T>()`. Configure timeout, BaseUrl and applicable credentials in options. Store secrets outside source control.
- Standard auth handler choices: API key header, bearer token, Basic auth, or custom OAuth/mTLS. Use `IOptionsMonitor<T>` as applicable; do not put auth logic inline in clients. Register typed clients and handlers in `AddInfrastructure()` with standard resilience.
- Use Scrutor for service decorators (e.g. caching, logging, timing, instrumentation) at registration in `AddInfrastructure()`, without changing the wrapped implementation or consumers. Do not use it for retry.

## Jobs

- Job code lives in `Infrastructure/JobHandlers/`; TickerQ only for background work, no IHostedService/IHostedLifecycleService for background jobs. `{Verb}Job` suffix for every TickerQ handler. `JobInitializer` is the sole `IHostedService` startup exception and stays in that folder.
- TickerQ jobs create their own DI scope and never capture the root scope. Resolve and call Application UseCases; do not put business logic in job handlers.
- Cron jobs can call UseCases through a created scope. Self-rescheduling jobs schedule the next `TimeTickerEntity` run based on work count. A single `JobInitializer` per application registers cron entities and starts self-rescheduling jobs; register it in `AddInfrastructure()`.

## Presentation and APIs

- Controllers only; never Minimal APIs. Use Swashbuckle and `Asp.Versioning.Mvc` URL-segment versioning (`/api/v1/orders`). Keep controllers thin: inject/call UseCases, no business logic.
- Return ProblemDetails for all API errors. Use ProblemDetails middleware; map exceptions there.
- Configure CORS explicitly in `AddInfrastructure()`. Never wildcard `AllowAnyOrigin()` in production. Name policies by purpose, allow only required origins/credentials/methods/headers, optionally set preflight max-age; wire middleware after `UseRouting()`.

## Caching, secrets, health checks

- Use `IDistributedCache` with StackExchange.Redis. Cache-aside: check, load on miss, store, return. After successful Commands evict stale entries; remove and repopulate on next read rather than updating cache directly.
- Serialize with `System.Text.Json`, not binary serializers. Set absolute or sliding TTL per entry, not a global TTL. Register in `AddInfrastructure()` using connection string from options.
- Development secrets: `dotnet user-secrets` in Presentation. Production: `@SecretName@` placeholders in appsettings replaced by Azure Library pipeline substitution. Never commit secrets or real connection strings.
- Use `Microsoft.Extensions.Diagnostics.HealthChecks` for liveness/readiness/startup. Check each infrastructure dependency (DB, Redis, external APIs); add custom checks for business-critical dependencies. Register in Infrastructure and map health endpoints in Presentation, separating live (no dependency predicate), ready (ready-tagged checks), and general health.

## Async, feature flags, quality, package management

- Propagate `CancellationToken` through every async call chain; name it `ct`; never default it to `CancellationToken.None`.
- Use Microsoft.FeatureManagement for feature toggles in `appsettings.json` under `FeatureManagement`; use `IFeatureManager` or `[FeatureGate]`. Register via `services.AddFeatureManagement()` in `AddInfrastructure()`. Do not remove old flag code until the flag is stable and fully rolled out.
- Root `.editorconfig`; SonarAnalyzer.CSharp and treat warnings as errors in CI. Enable `<ImplicitUsings>enable</ImplicitUsings>` and `<Nullable>enable</Nullable>` in every csproj.
- Use root `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`; pin dependency versions centrally and omit versions from csproj PackageReferences.

# .NET Testing Guidelines

## Test project conventions

- Architecture tests: dedicated `*.Architecture.Tests` project; enforce Clean Architecture rules in code.
- Domain: unit tests only; no DB, HTTP, filesystem, or infrastructure dependencies.
- Application: unit tests; mock external boundaries.
- Infrastructure: integration tests only; use Testcontainers for external dependencies such as databases and brokers.
- Mirror SUT folder structure: `src/{Project}/{Subfolder}/Component.cs` → `tests/{Project}.Tests/{Subfolder}/ComponentTests.cs`. `VerifyChecksTests.cs` is the exception and stays at test root because it is infrastructure, not SUT-mapped.

## Test dependencies

- For new solutions, use the latest stable MSTest V4 packages (`MSTest.TestFramework` and `MSTest.TestAdapter`), plus Moq and Shouldly. Do not choose TUnit or xUnit for new solutions. Existing solutions may retain their established test framework unless asked to migrate.
- Architecture tests additionally use NetArchTest to enforce layer references, naming conventions, and project references.
- Verify for snapshot/approval tests and Bogus for fake data are optional when appropriate.

## Architecture tests

Cover at least: Domain references no other project; Application references Domain but not Infrastructure; Presentation does not directly reference Domain or Infrastructure; UseCases follow `*Command`/`*Query` naming and `IUseCase<TIn,TOut>` contract.

## Unit and integration test class conventions

- Each test class has `_sut` (nullable, initialized with `null!`), one `private readonly Mock<T>` per dependency, and a constructor calling `CreateSut()`.
- `CreateSut()` resets/freshens state and constructs `_sut`.
- Never mock `IOptions<T>`; use `Options.Create(new OptionsType { ... })` and pass it directly.
- Test method name: `{SutMethodName}_When_{Scenario}_Then_{ExpectedResult}` (e.g. `Validate_When_DateMissing_Then_ReturnsError`).
- Test structure: Arrange, Act, blank line, Assert; use Shouldly assertions.
