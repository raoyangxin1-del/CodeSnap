# Bundled dependencies

These two assemblies are required by `CodeSnap.App.cs` for the Windows Runtime
WinRT projections (`Windows.Media.Ocr`, `Windows.Graphics.Imaging`, …):

| File | Source | License |
|---|---|---|
| `Microsoft.Windows.SDK.NET.dll` | Microsoft Windows SDK .NET projections (`Microsoft.Windows.SDK.NET.Ref`) | MIT |
| `WinRT.Runtime.dll` | CsWinRT | MIT |

They are bundled to make the app runnable out of the box. If you prefer to use
your own copies, replace the files in this directory or remove them — the
launcher also searches the PowerShell runtime directory.

Project: https://github.com/microsoft/CsWinRT