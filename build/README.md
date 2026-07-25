# Build & Packaging

## Prerequisites

- .NET 8 SDK (for test projects)
- .NET Framework 4.7.2 SDK (for Excel-DNA add-in)
- Visual Studio 2022+ (optional, for IDE development)

## Quick Build

```bash
dotnet build BomAddIn.sln
```

## Run Tests

```bash
dotnet test BomAddIn.sln
```

## Release Build & Pack

### Windows (PowerShell)

```powershell
.\build\scripts\build.ps1 -Configuration Release
```

### Windows (Batch)

```cmd
build\scripts\build.bat Release
```

The script performs: restore → build → test → ExcelDnaPack → output.

Output directory: `build\output\Release\`

## Manual Pack

If ExcelDnaPack.exe is not found, manually:

1. Build the solution in Release mode
2. Copy all files from `src\BomAddIn\bin\Release\net472\` to a distribution folder
3. Include:
   - `BomAddIn-AddIn.xll` (or `BomAddIn-AddIn64.xll` for 64-bit)
   - All `BomAddIn*.dll` files
   - `*.dna` config files
4. ZIP the folder for distribution

## Distribution Checklist

- [ ] Build passes (0 errors)
- [ ] All tests pass (147 tests)
- [ ] `.xll` file generated
- [ ] All dependency DLLs included
- [ ] License file included
- [ ] README included

## Authenticode Signing (Optional)

```powershell
signtool sign /fd SHA256 /a /f certificate.pfx /p <password> .\build\output\Release\BomAddIn-AddIn-packed.xll
```
