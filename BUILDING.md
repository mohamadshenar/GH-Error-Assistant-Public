# Repeatable Windows build

Use PowerShell 7 and SDK 10.0.401 from global.json. Plugin: net10.0-windows. Offline tests: net10.0. Pinned RhinoCommon/Grasshopper compile references: 8.19.25132.1001; these do not qualify Rhino 8. Host SDK/runtime DLLs are not redistributed.

This export contains approved production source, sanitized scalar fixtures and public documentation, without private Git history. Its future public commit differs from the private build revision. Use the public commit for new builds. The recorded distributed-binary revision is not a promise of bit-for-bit reproduction from the export.

```powershell
$revision = git rev-parse HEAD
dotnet restore GHErrorAssistant.sln -p:RhinoSdkPath=
dotnet build GHErrorAssistant.sln -c Release --no-restore -p:RhinoSdkPath= -p:SourceRevisionId=$revision
dotnet test tests/GHErrorAssistant.Tests -c Release --no-build
python -m unittest discover -s tests/NativeStage2 -p 'test_*.py' -v
./tests/Packaging/Privacy.Tests.ps1
./tests/Packaging/Candidate.Tests.ps1
./build-package.ps1 -SourceCommit $revision
./tests/Packaging/Archive.Tests.ps1
# Optional local packaging with an existing Rhino 9 Yak CLI:
./build-yak.ps1
```

Packaging neither installs nor publishes. Yak infers the older SDK API baseline; build-yak.ps1 narrows the newly generated .NET 10 package to rh9_0-win and recomputes normalized-manifest checksums. Never distribute inferred Rhino 8 staging packages. New binaries require appropriate testing; automated builds do not establish native acceptance.
