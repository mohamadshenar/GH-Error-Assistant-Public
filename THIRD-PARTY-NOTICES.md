# Third-party notices

MIT applies to this project's contributions and original branding. Rhino, Grasshopper and their SDKs remain owned/licensed by McNeel; Eto and framework libraries retain their own licenses. Rhino/Grasshopper names and contextual host UI are not relicensed as project artwork. This project is not endorsed by McNeel.

The runtime package contains the assistant GHA and allowlisted documentation/images only. Rhino supplies RhinoCommon, Rhino.UI, Grasshopper, GH_IO and Eto. Windows/.NET supplies framework facilities. No vendor DLL, SDK, test runner or .NET runtime is redistributed.

Compile-time NuGet references: RhinoCommon and Grasshopper 8.19.25132.1001. SDK/framework references are development-only; xUnit and Microsoft.NET.Test.Sdk are test-only. The Rhino 9 candidate no longer requires the net48 reference-assembly package. Compile reference versions do not establish native host compatibility.

Rhino is separately licensed. No assumption is made that vendor SDKs are MIT merely because project code is MIT. Re-review redistribution terms before adding any dependency to the package.
