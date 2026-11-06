# Licensing and dependency notices

GH Error Assistant source is offered under the included MIT license, with owner-confirmed original contribution provenance. Copyright (c) 2026 GH Error Assistant contributors. The original conversation/dependency logo is drawn in BrandAssets.cs; no external font or icon library is bundled. The screenshot shows only a disposable test fixture and host UI.

| Dependency | Version | Role / licensing observation | Bundled? |
| --- | --- | --- | --- |
| RhinoCommon, Grasshopper, GH_IO | 8.19.25132.1001 compile references | McNeel copyright; installed NuGet manifests do not state SPDX terms. Not covered by project MIT. Rhino is separately licensed. | No |
| Eto, Rhino.UI, System.Drawing, Windows.Forms | Host/framework provided | Host/platform APIs; no copied vendor implementation or font files. | No |
| Microsoft.NETFramework.ReferenceAssemblies.net48 | 1.0.3 | Development reference pack, vendor terms linked from NuGet manifest; not a runtime redistribution. | No |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT per exact installed NuGet manifest. | No |
| xunit | 2.9.2 | Apache-2.0 per exact installed NuGet manifest. | No |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 per exact installed NuGet manifest. | No |

Source includes package references, not these packages' source or binaries. Downstream dependency redistribution requires its own applicable notices/terms. The release contains only the project's GHA, project documentation/images and manifest; no vendor SDK or test assemblies. Compile/runtime framework compatibility is not native support evidence.

Authoritative references: https://www.nuget.org/packages/RhinoCommon/8.19.25132.1001 and https://www.nuget.org/packages/Grasshopper/8.19.25132.1001 ; xUnit license at https://github.com/xunit/xunit/blob/v2-2.9.2/license.txt ; test SDK license at https://github.com/microsoft/vstest/blob/main/LICENSE . McNeel SDK redistribution rights are not inferred from requireLicenseAcceptance=false. Do not add SDK DLLs without reviewing vendor permission. This review does not certify rights in unidentified historical contributions.
Rhino and Grasshopper are registered trademarks of Robert McNeel & Associates. Contextual host UI is not project-owned MIT artwork. No endorsement. Current SDK license: https://developer.rhino3d.com/en/license/ . No vendor runtime implementation is redistributed.