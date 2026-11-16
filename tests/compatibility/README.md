# .NET 11 test entry point

This directory selects SDK 11.0.1xx independently of the repository's SDK 10
policy. From here, run the existing tests on .NET 11:

```sh
dotnet test ../../FlowOnRails.slnx -c Release -p:FlowOnRailsTestTargetFramework=net11.0
```

The library remains `net10.0` and compiler tools remain `netstandard2.0`.
For isolated NuGet consumers and C# 15 scenarios, see
[compatibility](../../docs/compatibility.md) and [package tests](../packaging/README.md).
