# DiagKit.Dbc

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/DiagKit.Dbc?label=NuGet&color=orange)](https://www.nuget.org/packages/DiagKit.Dbc)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![CI](https://github.com/ThisIsJustNobody/DiagKit.Dbc/actions/workflows/ci.yml/badge.svg)](https://github.com/ThisIsJustNobody/DiagKit.Dbc/actions/workflows/ci.yml)

[简体中文](README.zh-CN.md)

DiagKit.Dbc is a .NET 10 DBC runtime library for diagnostic and CAN/CAN FD tooling. It focuses on DBC loading, metadata modeling, bit-level signal encoding and decoding, runtime channel state, signal samples, and hardware-agnostic periodic transmit scheduling.

## Layout

```text
src/DiagKit.Dbc              Product library and NuGet package content
src/DiagKit.Dbc.Workbook     DBC Excel format extension package
src/DiagKit.Dbc.Tool         DBC Excel export/import/validate CLI tool
tests/DiagKit.Dbc.Tests      Unit, conformance, runtime, and fuzz tests
tests/DiagKit.Dbc.Workbook.Tests  Workbook export/import tests
tests/DiagKit.Dbc.Tool.Tests      CLI tests
tests/DiagKit.Dbc.Benchmarks Benchmark, soak, and DBC corpus verification harness
DiagKit.Dbc.slnx             Solution
```

## Core Scope

- DBC loader with `Strict` / `Lenient` structured diagnostics.
- Diagnostic formatting/grouping, `Errors` / `Warnings` helpers, `SignalPath`, and non-hot-path `DbcSimpleRuntime` / `DbcSimpleChannel` entry points.
- Node, Message, Signal, environment variable, value table, attribute, multiplexing, and source-line metadata.
- Vector `System*LongSymbol` compatibility: `Name` uses the full long symbol when present, while `SourceName` / `NameAliases` keep the structural short name usable.
- Normalized DBC export through `DbcWriter`, with write diagnostics, Vector long-symbol output, and reload semantic equivalence checks.
- Semantic `DbcDocumentBuilder` support for creating or editing documents before export.
- CAN/CAN FD frame identifiers, DLC, flags, and timestamp models.
- Intel/Motorola signal codec, raw/physical conversion, and explicit write policies.
- Runtime sessions and channels for receive processing, current snapshots, and signal sample streams.
- Hardware-agnostic immediate frame building and periodic due-frame polling.
- Semantic mappings for cycle time, send type, timeout, signal start value, and CAN FD frame format.

The core library does not depend on CanHub or any hardware SDK. Applications adapt hardware frames to `DbcFrameView` for receive processing and consume due transmit frames through `IDbcFrameSink`.

## Entry Points

| Scenario | Start with | Notes |
| --- | --- | --- |
| First use, UI, scripts, tests | `DbcSimpleRuntime` | Loads a DBC, keeps diagnostics, and exposes `"Message.Signal"` convenience APIs. |
| Production runtime state machine | `DbcRuntimeSession` / `DbcChannelRuntime` | Use pre-resolved handles, snapshots, sinks, and periodic polling. |
| Low-level tools and metadata | `DbcLoader.LoadDocumentOrThrow`, `DbcDocument`, `DbcCodec` | Inspect DBC metadata or run stateless encode/decode without a runtime session. |

See [API usage guide](docs/API.zh-CN.md).
See [CanHub / external hardware adapter boundary](CANHUB-ADAPTER.zh-CN.md).

## Normalized DBC Export

`DbcWriter` generates stable, reloadable DBC text from an immutable `DbcDocument`. It is intended for newly built documents, semantic edits, and CI normalized exports with reload semantic equivalence. It is normalized export, not byte-for-byte round-trip editing: original whitespace, statement order, unknown statements, and comment placement are not preserved.

By default, `DbcWriterCompatibilityProfile.ReloadEquivalent` preserves metadata that this library can reload. That can include statements that are not currently known-good in Vector CANdb++, such as general `BA_ ... EV_ ...` environment-variable attribute assignments and `BA_REL_` relation assignments. For files intended to be opened in CANdb++, use `DbcWriterCompatibilityProfile.CanDbPlusKnownGood`: strict mode fails on those known-unsupported statements, while lenient mode omits them and returns warnings. The current CANdb++ known-good set includes `EV_`, `BO_TX_BU_`, `BA_DEF_REL_`, and `BA_DEF_DEF_REL_`; `BA_REL_` assignments remain unsupported until a verified Vector/CANdb++ sample or official grammar is available.

```csharp
var document = DbcLoader.LoadTextDocumentOrThrow(dbcText);
var result = DbcWriter.WriteText(document);
File.WriteAllText("normalized.dbc", result.GetTextOrThrow());
```

Use `DbcDocumentBuilder` when creating or editing documents before export:

```csharp
var builder = DbcDocumentBuilder.Create();
builder.AddNode("ECU");
builder.AddMessage(new DbcRawMessageId(256), "Status", 8, "ECU")
    .AddSignal("Speed", 0, 16)
    .WithScaling(0.1, 0);

var text = DbcWriter.WriteTextOrThrow(builder.Build());
```

## DBC Excel Format

Excel editing lives in the optional `DiagKit.Dbc.Workbook` extension package; the core `DiagKit.Dbc` package does not expose Excel APIs. The `.xlsx` file is a generic DBC semantic table format: it contains DBC entity sheets such as `Network`, `Nodes`, `Messages`, `Signals`, `ValueDescriptions`, `MultiplexRanges`, `EnvironmentVariables`, `AttributeDefinitions`, `Attributes`, and relation attribute sheets, with no manifest, readme sheet, source path/hash, or internal object keys.

Create a blank template or export one `.xlsx` from a DBC, edit the DBC semantic tables, then import that Excel file by itself and write normalized DBC output. This is not CAN trace data, signal samples, EOL test scripts, or source-preserving DBC round-trip editing. Import still runs through `DbcWriter` validation; `Vector__XXX`, `VFrameFormat`, `Gen*` timing/send-type metadata, and Vector independent signals are normalized to the current DBC writer capability.

```csharp
using DiagKit.Dbc.Workbook;

var document = DbcLoader.LoadDocumentOrThrow("vehicle.dbc");
DbcWorkbookExporter.WriteWorkbookOrThrow("edit.xlsx", document);

var imported = DbcWorkbookImporter.ImportWorkbookFile("edit.xlsx").GetDocumentOrThrow();
var normalized = DbcWriter.WriteTextOrThrow(imported);
```

CLI:

```bash
diagkit-dbc workbook template -o edit.xlsx
diagkit-dbc workbook export vehicle.dbc -o edit.xlsx
diagkit-dbc workbook validate edit.xlsx
diagkit-dbc workbook import edit.xlsx -o normalized.dbc
```

## Quick Start

```csharp
var simple = DbcSimpleRuntime.LoadFile("vehicle.dbc");
Console.WriteLine(DbcDiagnosticFormatter.FormatGrouped(simple.LoadResult.Diagnostics));
var clockStart = Stopwatch.GetTimestamp();
simple.SetPhysicalValue("VehicleStatus.VehicleSpeed", 42.5);
var frame = simple.BuildFrame("VehicleStatus", DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(clockStart)));
```

## Build and Test

```bash
dotnet build DiagKit.Dbc.slnx
dotnet test DiagKit.Dbc.slnx
```

## Verification Harness

```bash
dotnet run --project tests/DiagKit.Dbc.Benchmarks/DiagKit.Dbc.Benchmarks.csproj -- --matrix
dotnet run --project tests/DiagKit.Dbc.Benchmarks/DiagKit.Dbc.Benchmarks.csproj -- --soak --seconds 30
dotnet run --project tests/DiagKit.Dbc.Benchmarks/DiagKit.Dbc.Benchmarks.csproj -- --corpus path/to/dbc-folder
```

## Package Status

The project uses the MIT license. NuGet package versions are generated by MinVer from Git tags with `v` as the tag prefix; this repository does not hard-code release versions in the project file. The next planned preview tag is `v1.2.0-preview.1`. CI runs for pull requests and pushes to `main`, `master`, and `release/**`; tag pushes drive NuGet publishing and GitHub Release creation.

See [CHANGELOG.md](CHANGELOG.md), [CONTRIBUTING.md](CONTRIBUTING.md), and [SECURITY.md](SECURITY.md) for release notes, contribution guidance, and vulnerability reporting.
See [publishing checklist](docs/PUBLISHING.zh-CN.md) for release steps.
