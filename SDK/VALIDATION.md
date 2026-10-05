# SDK maintainer checks

From the loader source root:

```powershell
.\tests\SDK.Tests.ps1 -GameDir 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers2\Game2'
.\tools\Build-SDK.ps1
```

The test builds and extracts the SDK ZIP, installs its template in an isolated
template hive, generates a renamed project, builds its player ZIP, checks the
archive contents, and passes the resulting DLL through the real loader catalog.
It constructs the plugin against the installed native PluginHost, invokes the
starter callbacks, and checks that repeated disposal removes both subscriptions.
It also checks rejection of an invalid game directory.

The package script uses an explicit file list and refuses to overwrite an
existing ZIP. Pass a new OutputDirectory when preparing another build.

Validated on 2026-10-04 against SE2 2.4.0.95 with .NET SDK 10.0.401 targeting
net9.0-windows. No game launch, installation, or in-world acceptance was performed.
The smoke plugin writes its lifecycle log under APPDATA/SpaceEngineers2/Plugins/SDKSmokePlugin.
Test artifacts remain under artifacts/sdk-test-*.
