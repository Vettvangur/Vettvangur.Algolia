# Offline indexing regressions

Run from the repository root:

```powershell
dotnet run --project tests/Vettvangur.Algolia.Tests -f net8.0 -p:GeneratePackageOnBuild=false
dotnet run --project tests/Vettvangur.Algolia.Tests -f net10.0 -p:GeneratePackageOnBuild=false
```

The executable harness does not connect to a database or Algolia. Both targets
verify that the executor reads configured languages through the supported Umbraco
service and caches them. The .NET 10 target uses Umbraco 17.8's actual block index
factory to reproduce the legacy empty-language crash and verify text extraction
for variant/invariant blocks in both processing orders.

Reflection accesses the executor's private methods without booting Umbraco. Only
the language and leaf text-editor dependencies are mocked; block parsing and
nested extraction use the real Umbraco serializer and indexing factory.
