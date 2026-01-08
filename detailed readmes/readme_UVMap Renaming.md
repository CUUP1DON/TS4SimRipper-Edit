# TS4 SimRipper UV Map Naming Fix - Change Log

## Overview
This document summarizes the changes made to fix the UV map naming issue in the TS4 SimRipper program. The program was previously renaming UV maps from their original names (`uv_0`, `uv_1`) to names based on mesh names (like `Top-mesh-map-0`, `Mascara_glass-mesh-map-0`, etc.).

## Changes Made

### 1. ColladaDAE.cs File Updates
**File**: [`ColladaDAE.cs`](../TS4 SimRipper/src/ColladaDAE.cs)

#### Key Changes:

**Source ID Generation (Lines 939-942, 984)**
- **Before**: `meshName + "-map-" + uvIndex`
- **After**: `"uv_" + uvIndex`

**Texture Coordinate References (Lines 847, 849, 851)**
- **Before**: mesh-specific names
- **After**:  `uv_0`, `uv_1` naming

**Bind Vertex Input Semantic (Line 1126)**
- **Before**: `meshName + "-map-" + uvIndex`
- **After**: `"uv_" + uvIndex`

#### Specific Code Changes:

```csharp
// Lines 939-942: Source ID generation
string sourceId = "uv_" + uvIndex;  // Changed from meshName + "-map-" + uvIndex

// Line 984: UV source references
string uvSourceId = "uv_" + uvIndex;  // Changed from meshName + "-map-" + uvIndex

// Lines 847, 849, 851: Texture coordinate references
// Changed from mesh-specific names to uv_0, uv_1

// Line 1126: Bind vertex input semantic
semantic = "uv_" + uvIndex;  // Changed from meshName + "-map-" + uvIndex
```

## Technical Details

### Collada DAE Format Requirements
The Collada format requires unique identifiers for each source element.

## Files Modified
- [`ColladaDAE.cs`](../TS4 SimRipper/src/ColladaDAE.cs) - Primary changes for UV map naming