# DirectStorage Updater

Lightweight standalone tool (.exe) that scans current DirectStorage .dll files, checks for available updates, perform backup of the existing version of the files and replaces them with newer version if available. Designed to simplify maintenance and keep DirectStorage components up to date without manual file handling.

<img title="Program" alt="Program" src="./screenshots/Horizon Forbidden West.png" width="100%">

More info: https://www.nexusmods.com/games/site/mods/1982

## Features

- Reads the FileVersion of `dstorage.dll` and `dstoragecore.dll` from the game folder.
- Fetches all available versions from NuGet at runtime.
- Flags the latest stable version as default/recommended.
- Preview versions are listed but flagged as not recommended.
- Backs up current .dll files before any update.
- Downloads the selected `.nupkg` from NuGet, extracts only the two needed .dll files.
- On startup, detects any existing backups and asks user to restore.
- Shows cumulative changelog from your current version to the version you updated to.
- Automatically checks for updates

## Requirements

- Windows x64.
- .NET 8 Runtime **or** publish as self-contained (see below).
- Internet access to `api.nuget.org`.

## Build

```bash
cd DirectStorageUpdater
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\DirectStorageUpdater.exe`

Or if you want a smaller binary that requires .NET 8 to be installed:

```bash
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

## Usage

1. Copy `DirectStorageUpdater.exe` into the game's root folder (same folder as `dstorage.dll` and `dstoragecore.dll`)
2. Run it

## NuGet package DLL path

DLLs are extracted from inside the `.nupkg` at:
```
native/bin/x64/dstorage.dll
native/bin/x64/dstoragecore.dll
```

## Notes

- The tool reads the **FileVersion** from the .dll present at runtime. 
- NuGet version list and changelog is fetched live.
- If extraction fails for whatever reason, the backup is automatically restored.

## Support

If you encounter any problems or have suggestions, please [open an issue](https://github.com/ExiledEye/DirectStorageUpdater/issues).

## License

Copyright (c) 2026 Exiled Eye  
This project is licensed under the MPL-2.0 License.  
Refer to the [LICENSE](LICENSE) file for details.
