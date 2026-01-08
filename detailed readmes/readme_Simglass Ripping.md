# SimRipper Alpha Mesh Extraction Bug Fix - README 3

## Issue Identified
**Problem**: When the program rips a sim with the setting 'All separate meshes, one texture', it does not extract alpha meshes (Simglass). However, alpha meshes extract correctly when set to 'Single mesh and texture'.

## Root Cause Analysis
The bug is located in the `SaveModelMorph` method in [`PreviewControl.cs`](TS4 SimRipper/src/PreviewControl.cs:1602), specifically in the "SeparateMeshesByPart" branch (lines 1641-1681).

### Problematic Code Section
In the "All separate meshes, one texture" mode (index 1), the original code only saves `CurrentModel` meshes and completely ignores `GlassModel` and `WingsModel`:

```csharp
else if (SeparateMeshesByPart)                                             //all separate meshes
{
    for (int i = CurrentModel.Length - 1; i >= 0; i--)
    {
        if (CurrentModel[i] != null)  // <-- Only checks CurrentModel!
        {
            GEOM tmp = new GEOM(CurrentModel[i]);
            tmp.AppendMesh(CurrentModel[i]);
            geomList.Add(tmp);
            nameList.Add(partNames[i]);
        }
        // GlassModel and WingsModel are NEVER checked!
    }
}
```

## Comparison with Working Modes

### Single Mesh Mode (Works Correctly)
The "Single mesh and texture" mode correctly iterates through all three arrays:
```csharp
if (SingleMesh)     //single mesh
{
    GEOM tmp = null;
    for (int i = CurrentModel.Length - 1; i >= 0; i--)   
    {
        if (CurrentModel[i] != null)
        {
            if (tmp == null) tmp = new GEOM(CurrentModel[i]);
            else tmp.AppendMesh(CurrentModel[i]);
        }
        if (GlassModel[i] != null)  // <-- Includes GlassModel
        {
            if (tmp == null) tmp = new GEOM(GlassModel[i]);
            else tmp.AppendMesh(GlassModel[i]);
        }
        if (WingsModel[i] != null)  // <-- Includes WingsModel
        {
            if (tmp == null) tmp = new GEOM(WingsModel[i]);
            else tmp.AppendMesh(WingsModel[i]);
        }
    }
    // ... rest of code
}
```

### Separate Meshes by Shader Mode (Works Correctly)
The "Solid mesh and glass mesh" mode also correctly handles all three mesh types:
```csharp
else                                                    //solid mesh and glass mesh
{
    GEOM solid = null;
    GEOM glass = null;
    GEOM wings = null;
    for (int i = CurrentModel.Length - 1; i >= 0; i--)
    {
        if (CurrentModel[i] != null)
        {
            if (solid == null) solid = new GEOM(CurrentModel[i]);
            else solid.AppendMesh(CurrentModel[i]);
        }
        if (GlassModel[i] != null)  // <-- Includes GlassModel
        {
            if (glass == null) glass = new GEOM(GlassModel[i]);
            else glass.AppendMesh(GlassModel[i]);
        }
        if (WingsModel[i] != null)  // <-- Includes WingsModel
        {
            if (wings == null) wings = new GEOM(WingsModel[i]);
            else wings.AppendMesh(WingsModel[i]);
        }
    }
    // ... rest of code
}
```

## Proposed Fix
The fix involves modifying the "SeparateMeshesByPart" branch to include checks for `GlassModel` and `WingsModel` arrays, similar to how the other modes handle them.

### Fixed Code Structure
```csharp
else if (SeparateMeshesByPart)                                             //all separate meshes
{
    for (int i = CurrentModel.Length - 1; i >= 0; i--)
    {
        if (CurrentModel[i] != null)
        {
            GEOM tmp = new GEOM(CurrentModel[i]);
            tmp.AppendMesh(CurrentModel[i]);
            geomList.Add(tmp);
            nameList.Add(partNames[i]);
        }
        if (GlassModel[i] != null)  // <-- ADD THIS CHECK
        {
            GEOM tmp = new GEOM(GlassModel[i]);
            tmp.AppendMesh(GlassModel[i]);
            geomList.Add(tmp);
            nameList.Add(partNames[i] + "_glass");
        }
        if (WingsModel[i] != null)  // <-- ADD THIS CHECK
        {
            GEOM tmp = new GEOM(WingsModel[i]);
            tmp.AppendMesh(WingsModel[i]);
            geomList.Add(tmp);
            nameList.Add(partNames[i] + "_wings");
        }
    }
}
```

## Files Affected
- **Primary File**: [`TS4 SimRipper/src/PreviewControl.cs`](TS4 SimRipper/src/PreviewControl.cs)
- **Method**: `SaveModelMorph` (lines 1602-1801)
- **Specific Section**: Lines 1641-1681 (SeparateMeshesByPart branch)