# Installation, upgrade and rollback

Target: Windows / Rhino 9 / default .NET 10 / Grasshopper 1. No Rhino 8 support. Stable 3.1.0 is installable only after publication; RC1 remains a distinct prerelease.

## Package Manager (recommended)

1. Save your work. Record the installed assistant version and preserve its package/GHA and checksum outside discovery.
2. Identify any manual assistant copy. With Rhino closed, move only that known GHA outside discovery before using Yak. Do not delete unknown files or reset settings.
3. Run **PackageManager** in Rhino 9; search **gh-error-assistant**, select exactly **3.1.0**, install and restart when requested. **Include pre-releases** is needed for RC1, not stable.
4. Start Grasshopper and **Error Assistant → Open assistant**. Check the installed package version. Assembly version alone does not distinguish RC1 from stable; use product version and checksum when investigating conflicts.

If stable is absent, check publication status, Rhino version and runtime. Never force the Rhino 9 distribution into Rhino 8.

## Manual Windows ZIP (alternative)

Verify the archive against published SHA256SUMS.txt. Close Rhino, extract, and use Grasshopper **File → Special Folders → Components Folder** to identify the manual library. Copy only GHErrorAssistant.gha. Keep documentation separately. Restart Rhino. Do not combine manual and managed copies or overwrite a loaded assembly.

## Remove or recover

For Yak, use **PackageManager → Installed → gh-error-assistant → Uninstall**, then restart/close Rhino as requested. For manual installation, close Rhino and remove only the identified assistant GHA from discovery. Leave unrelated plugins and settings unchanged.

After removing a newer managed version, restore the preserved prior package through the supported package mechanism. Rhino's Yak CLI supports installing a verified local .yak file. Restore a manual backup only for the manual route with all Rhino processes closed. Verify its checksum, restart and confirm one discoverable assistant. Do not blindly restore old settings over newer work.

If a serious regression occurs, stop the affected workflow, preserve available diagnostics and reproduction steps, return to the verified prior version and report the issue with private data removed. No licensing or unrelated-plugin change is part of recovery.
