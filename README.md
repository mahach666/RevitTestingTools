# RevitTestingTools

Tools for automated testing of Autodesk Revit plugins. It has two parts that work together or on their own.

RevitTestHost runs tests inside a live Revit process. You need the add-in and a test assembly of your plugin: tests get a `Document`, transactions with rollback and `.rvt` models, and the result is written as JSON. A run can be started from a ribbon button, from the console, or through a named pipe.

RevitUITesting drives the plugin UI from outside Revit through UI Automation, the way a user would. It hands checks inside the model over to RevitTestHost through the pipe. The library does not reference the Revit API.

Version 0.1.0, early. Verified live on Revit 2020.2, 2022, 2025.1 and 2027.2, and the public API may change before 1.0.

## Why

The `RevitAPI.dll` package from NuGet ships as a reference assembly: instance methods throw `NotImplementedException` unless Revit is running. Plugin logic that does not touch Revit is covered by plain `dotnet test`. Everything else (`Document`, `Element`, `Transaction`, windows, ribbon, panes) cannot be tested without a live Revit. RevitTestHost runs such tests inside Revit, and RevitUITesting checks windows, ribbon and panes from outside.

## What it covers

- Unit-level tests of code that uses Revit API types and cannot run outside Revit (`[RevitFact]`, no model). Pure logic that does not touch Revit is still tested with plain `dotnet test`.
- Integration tests against a real model, a new project or the active document (`[RevitTest(Model = ...)]`), with transactions that roll back.
- End-to-end UI tests that drive ribbon, windows, panes and dialogs through UI Automation and check the result inside the model (RevitUITesting).

Not available yet: parameterized tests (`[RevitTheory]`), model-level fixtures, TRX reports.

## Components

| Project | Platform | What it does |
|---|---|---|
| `src/RevitTestHost.Sdk` | netstandard2.0 | the `[RevitFact]`, `[RevitTest]` and `[TestModel]` attributes and the test context. NuGet package |
| `src/RevitTestHost.Core` | net47, net8.0-windows | JSON, report, test selection, protocol and pipe server. No Revit API |
| `src/RevitTestHost.Addin` | net47, net48, net8.0-windows or net10.0-windows (by Revit year) | Revit add-in with a ribbon button, a CLI mode and a server mode |
| `src/RevitTestHost.Cli` | net8.0 | starts Revit with tests from the console, exit code is the number of unsuccessful tests |
| `src/RevitUITesting` | net48 | UI test library: waits, ribbon, windows, panes, dialogs, failure artifacts. NuGet package |
| `tools/RevitUITesting.Inspector` | net48 | console inspector of the UI Automation tree |
| `samples/RevitTestHost.SampleTests` | net47, net48, net8.0-windows or net10.0-windows (by Revit year) | sample test project for RevitTestHost |

## How the parts work together

1. A UI test starts Revit with the `REVITTESTHOST_SERVE` variable.
2. The RevitTestHost add-in opens a named pipe and waits for commands.
3. The test presses buttons and enters data through UI Automation.
4. To check the result in the model, the test sends a `run` command with a filter. The Revit test runs inside Revit and the report comes back to the test.

## Requirements

- Windows with Revit installed. The add-in is built for Revit 2020 to 2027: each year has its own build and target framework (.NET Framework 4.7 for 2020, 4.8 for 2021 to 2024, .NET 8 for 2025 and 2026, .NET 10 for 2027). Only Revit 2022 has been verified in a live Revit so far, see [docs/versions.md](docs/versions.md) (in Russian) for the matrix.
- A .NET SDK to build (checked with SDK 9 and 10). SDK 10 is needed for Revit 2027 and adds the net10.0-windows run of the host tests; SDK 8, 9 and 10 are installed in CI. `RevitTestHost.Cli` needs the .NET 8 runtime.

| Revit | Runtime | Add-in target | Builds | Verified in a live Revit |
|---|---|---|---|---|
| 2020 | .NET Framework 4.7 | net47 | yes | not yet |
| 2021 to 2024 | .NET Framework 4.8 | net48 | yes | 2022 only |
| 2025, 2026 | .NET 8 | net8.0-windows | yes | not yet |
| 2027 | .NET 10 | net10.0-windows | yes | not yet |

## Quick start

### Tests inside Revit

Install the add-in: run `RevitTestHost-Addin-<version>.msi` from the release (per user, no admin rights; installs into `%AppData%\Autodesk\Revit\Addins\<year>\` for every installed Revit 2020-2027), or unpack the release archive for your Revit year into `%ProgramData%\Autodesk\Revit\Addins\<year>\` (for Revit 2027 into `%AppData%\Autodesk\Revit\Addins\2027\`: Revit 2027 does not load manifests from `%ProgramData%`), or build `RevitTestHost.Addin` in the Debug configuration, which copies the files itself. Then write tests:

```csharp
using RevitTestHost.Sdk.Attributes;
using RevitTestHost.Sdk.Contracts;

public class WallTests
{
    [RevitTest(Model = RevitTestAttribute.NewProject)]
    public void Wall_can_be_created(ITestContext ctx)
    {
        using (ctx.Transactions.Begin("create wall"))   // rolled back on Dispose
        {
            // ctx.Document: a fresh metric project
        }
    }
}
```

Put `MyPlugin.RevitTests.dll` into `...\Addins\2022\TestHost\Tests\` and run it from the console:

```powershell
RevitTestHost.Cli --revit-version 2022 --filter "MyPlugin.RevitTests.*" --output results.json
```

The exit code is the number of tests with the status `failed` or `error`. More details: [docs/revit-test-host.md](docs/revit-test-host.md).

### UI tests

A test class inherits `RevitUiTestBase`. The library starts Revit, closes stray windows before each test and saves failure artifacts (a screenshot, the window tree, the step log).

```csharp
[TestFixture, Explicit("live Revit"), Category("RevitUi")]
[RevitUiModel("@new")]
public class SearchPaneTests : RevitUiTestBase
{
    [Test]
    public void Pane_opens_from_ribbon()
    {
        App.Ribbon.Click("MyPlugin", "Search");
        DockablePanePage pane = App.Panes.Find("Family search");
        pane.WaitText(t => t.StartsWith("Project"));
    }
}
```

Project setup, configuration and the full list of features: [docs/revit-ui-testing.md](docs/revit-ui-testing.md).

## Build and test

```powershell
dotnet build RevitTestingTools.sln -c Release
dotnet test RevitTestingTools.sln -c Release --no-build
```

Unit tests run without Revit. The RevitUITesting contract tests need a live Revit (the year comes from the configuration) and are skipped by default. The script `tools/serve-smoke.ps1` checks the server mode in a live Revit and is run by hand.

Besides the usual work, a Debug build copies the add-in into `%ProgramData%\Autodesk\Revit\Addins\<version>\` (for 2027 into `%AppData%\Autodesk\Revit\Addins\2027\`). Use Release to build without that side effect.

## Repository layout

```text
src/        libraries, add-in and CLI
tests/      unit and contract tests
tools/      UI Automation inspector and check scripts
samples/    sample test project
docs/       documentation
```

## Documentation

The detailed documentation is written in Russian.

- [docs/revit-test-host.md](docs/revit-test-host.md): installing the add-in, attributes, the three ways to run, the report.
- [docs/versions.md](docs/versions.md): supported Revit versions, build per year, how test assemblies are loaded on .NET 8 and later.
- [docs/live-testing.md](docs/live-testing.md): how to run the live checks on another machine.
- [docs/server-protocol.md](docs/server-protocol.md): the server mode protocol, commands and errors.
- [docs/revit-ui-testing.md](docs/revit-ui-testing.md): the UI test library, configuration, the inspector.
- [docs/revit-quirks.md](docs/revit-quirks.md): what was found out about Revit windows, ribbon and panes, and what is still unverified.
- [CHANGELOG.md](CHANGELOG.md): changes by version.

## Releases

Each GitHub release attaches one add-in archive per Revit year (2020 to 2027), the CLI, the inspector, and the `RevitTestHost.Sdk` and `RevitUITesting` packages as `.nupkg` files. Until the packages are published on nuget.org, add them as a local NuGet source or reference the projects. The release procedure is described in [RELEASING.md](RELEASING.md).

## License

[MIT](LICENSE)
