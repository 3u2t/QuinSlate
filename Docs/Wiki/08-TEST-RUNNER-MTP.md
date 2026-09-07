# Test runner: the `global.json` MTP opt-in

> _Last updated: 2026-09-07_

`dotnet test QuinSlate.slnx -p:Platform=x64` only works because of the repo-root `global.json`:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

**Do not delete that file.** Without it the suite cannot be run through `dotnet test` at all.

## What breaks without it

The .NET 10 SDK still routes `dotnet test` through VSTest by default. Microsoft.Testing.Platform
v2 — which xUnit v3 pulls in — refuses to run under VSTest, so the command fails before a single
test executes:

```
Microsoft.Testing.Platform.MSBuild.targets(320,5): error : Testing with VSTest target is no
longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later. If you use dotnet test,
you should opt-in to the new dotnet test experience.
```

This is a toolchain mismatch, not a failing suite. If you see it, check `global.json` before
touching a test.

## Two things that look like the fix but are not

- **`dotnet.config`** with a `[dotnet.test.runner]` section — ignored; the .NET 10 opt-in lives in
  `global.json`.
- **`<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`** — the .NET 9 and
  earlier opt-in, which worked by running MTP *on top of* VSTest. MTP v2 removed that path, so the
  property changes nothing now.

## Fallback

The test project builds an executable with the MTP entry point compiled in, so it can always be
run directly without the SDK's test command:

```
QuinSlate.Tests/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/QuinSlate.Tests.exe
```

## Leftovers

`QuinSlate.Tests.csproj` still references `xunit.runner.visualstudio` and `coverlet.collector`.
Both are VSTest-only and unused on the MTP path; they are harmless, but do not reach for them
when wiring up coverage — the MTP equivalent is `Microsoft.Testing.Extensions.CodeCoverage`.
