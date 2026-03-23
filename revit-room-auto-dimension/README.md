# Revit Room Auto Dimension Plugin

This plugin adds a **one-click external command** that auto-dimensions every placed room in the active plan view.

## What it does

- Collects all placed rooms in the active view.
- Reads room boundaries from walls, room-separator lines, and other boundary curves.
- Uses interior wall faces plus door/window opening references on boundary walls when available.
- Creates face-to-face dimensions (horizontal and vertical where possible) through each room center.

## Build

1. Install Visual Studio with .NET Framework 4.8 targeting pack.
2. Set environment variable `REVIT_API_DIR` to your Revit install folder (contains `RevitAPI.dll` and `RevitAPIUI.dll`).
   - Example: `C:\Program Files\Autodesk\Revit 2025`
3. Build:

```powershell
dotnet build .\RoomAutoDimension.csproj -c Release
```

## Install

1. Copy `bin\Release\RoomAutoDimension.dll` to a stable folder, for example:
   `C:\RevitPlugins\RoomAutoDimension\RoomAutoDimension.dll`
2. Update `RoomAutoDimension.addin` `<Assembly>` path if needed.
3. Copy the `.addin` file to one of:
   - `%AppData%\Autodesk\Revit\Addins\2024`
   - `%AppData%\Autodesk\Revit\Addins\2025`

## Use

1. Open a floor/ceiling/engineering plan view with placed rooms.
2. Run command **Auto Dimension Rooms**.
3. The plugin creates dimensions for each room where valid wall-face references are found.

## Notes / limits

- Best results with orthogonal room boundaries.
- Some family types may not expose left/right/front/back references, so openings can vary by family content quality.
- Curved or unusual boundaries may produce fewer dimensions.
- Existing dimensions are not removed or updated.
