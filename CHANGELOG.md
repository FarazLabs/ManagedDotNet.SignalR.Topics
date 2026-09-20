# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-20

First public NuGet release of `ManagedDotNet.SignalR.Topics`.

### Added

- Topic hubs with a single `Handle(topic, payload)` entry point and modular `AddTopicHub` / `MapTopicHubs` registration
- Inbound `HandleOnServer` routing to DI `IHubCommandHandler<T>` with per-type deserializers and fluent topic `RequireAuthorization` / `AllowAnonymous` (handler type attributes are not read)
- Outbound `HandleOnClient` type→topic serializers via `TopicClientProxy` / `ITopicHubContext<THub>`
- Hub-level `RequireAuthorization` / `AllowAnonymous`, plus MapHub-parity conventions (`RequireCors`, `ConfigureHttpConnection`, host/display/metadata)
- Default JSON (de)serialization via `JsonSerializerDefaults.Web` (camelCase, case-insensitive)
- Optional `CancellationToken` on outbound `TopicClientProxy.Handle` forwarded to `SendAsync`
- Eager misconfiguration checks when registering incomplete `HandleOnServer` / `HandleOnClient` bindings
- Empty `RequireAuthorization(params …)` throws with a suggestion (avoids silent no-op)
- `HandleOnServer` / `HandleOnClient` / `WithHandler` constrained to `class`
- `MapTopicHubs` Warning when hub `RequireAuthorization` is combined with topic `AllowAnonymous`
