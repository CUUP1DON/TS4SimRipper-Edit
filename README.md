# TS4 SimRipper Classic
> [!IMPORTANT]
> This program was originally created by [CmarNYC](https://modthesims.info/d/635720/ts4-simripper-classic-rip-sims-from-savegames-v3-14-2-0-updated-4-19-2023.html) & is currently being maintained by [andrews4s](https://github.com/CmarNYC-Tools/TS4SimRipper).

> [!IMPORTANT]
> This program was modified using the help of claude.ai.

> [!WARNING]
> DO NOT create issues about this fork in the original repository! Use the [Issues](https://github.com/CUUP1DON/TS4SimRipper/issues) tab of THIS repository to report bugs or create feature requests.

## Requirements
This program requires [Microsoft .NET Desktop Runtime 10.0](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Download
To download SimRipper, go to [Releases](https://github.com/CmarNYC-Tools/TS4SimRipper/releases) and click the .zip on the latest.

## TS4 Sim Ripper Changelog (1.8.2026)
- Updated from .NET 6.0 to .NET 10.0
- Ripper will now restart when clicking the save button in the setup dialog to apply and load new directories & package files (should solve the floating heads issue)
- When ripping into collada DAE format, it keeps EA's uvmap naming (uv_0 & uv_1) instead of adding the mesh type (i.e. Top-mesh-map-0, RingMidLeft-mesh-map-1)
- Fixed issue with ripper not extracting simglass shader meshes when using 'All separate meshes, one texture'
- Fixed an issue with ripper overlaying duplicate meshes when exporting sims into the .DAE format
- Included a readme folder for detailed explanations on fixes applied
