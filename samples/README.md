# Samples

## RevitTestHost.SampleTests

A probe for the server mode. The test `ActiveDocumentProbe.TitleMatchesParameter`, marked `[RevitTest(Model = "@active")]`, compares the title of the active document with the `expectedTitle` parameter of the `run` command.

- Build without deploying: `dotnet build samples/RevitTestHost.SampleTests/RevitTestHost.SampleTests.csproj -c Release`.
- A Debug build copies only `RevitTestHost.SampleTests.dll` to `%ProgramData%\Autodesk\Revit\Addins\2022\TestHost\Tests\`. The project is not part of `RevitTestingTools.sln`: once deployed, the sample takes part in every run (the CLI without a filter, the ribbon button).
- The sample is used by the script `tools/serve-smoke.ps1` (filter `RevitTestHost.SampleTests.*`) and by the RevitUITesting contract tests.
- For another Revit year add `-p:RevitVersion=<year>`; the target framework is chosen automatically (see docs/versions.md).
