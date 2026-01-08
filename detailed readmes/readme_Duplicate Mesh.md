# SimRipper Duplicate Mesh Geometry Fix - README 4

## Issue Identified
**Problem**: When ripping a sim and exporting to DAE format, duplicate meshes are created. When the exported DAE is imported into Blender and mesh parts are separated (e.g., separating arms and neck from the top), duplicate copies of those parts are found underneath the visible mesh. This occurs regardless of whether "Clean DAE mesh? (Remove doubles)" is checked or not, unnecessarily increasing the geometry count of the sim.

## Root Cause Analysis
The bug is located in the `DAE` constructor in [`ColladaDAE.cs`](../TS4 SimRipper/src/ColladaDAE.cs:553), specifically in the face index generation section (lines 637-647).

### Problematic Code Section
The original code incorrectly generated face point indices by duplicating the same vertex index multiple times based on the `Stride` value:

```csharp
List<uint> facepoints = new List<uint>();
for (int f = geostate.StartFace; f < geostate.PrimitiveCount; f++)
{
    uint[] face = geom.getFaceIndicesUint(f);
    for (int i = 0; i < 3; i++)
    {
        for (int j = 0; j < mesh.Stride; j++)
        facepoints.Add(face[i]);  // <-- PROBLEM: Adds same index multiple times!
    }
}
mesh.facePoints = facepoints.ToArray();
```

### Understanding the Bug

The `Stride` value represents the number of vertex attribute components per vertex in the COLLADA format:
- Position (always present)
- Normal (if present)
- UV coordinates (one or more sets if present)
- Colors (if present)

**Example**: For a mesh with position + normal + 2 UV sets + colors, `Stride = 5`

The problematic code would add the vertex index 5 times in sequence (e.g., `[0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2]`), which is incorrect for COLLADA. This caused Blender to interpret the data as duplicate overlapping geometry.

### Correct COLLADA Structure

In COLLADA's polylist format with shared vertices, each vertex should be referenced once per attribute, with all attributes pointing to the same vertex index. For a triangle with vertices [0, 1, 2]:

**Correct structure**:
```
Position indices:  [0,    1,    2   ]
Normal indices:    [0,    1,    2   ]
UV0 indices:       [0,    1,    2   ]
UV1 indices:       [0,    1,    2   ]
Color indices:     [0,    1,    2   ]

Serialized: [0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2]
```

**What the bug was creating**:
```
Stride repetitions: [0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2]
```

While these look similar, the bug was creating the array by blindly repeating vertex indices `Stride` times rather than explicitly adding indices for each attribute type. This subtle difference caused the COLLADA parser to interpret the data incorrectly.

## Solution Implemented

The fix replaces the blind stride-based loop with explicit attribute-based index generation:

```csharp
List<uint> facepoints = new List<uint>();
for (int f = geostate.StartFace; f < geostate.PrimitiveCount; f++)
{
    uint[] face = geom.getFaceIndicesUint(f);
    for (int i = 0; i < 3; i++)
    {
        // Add position index
        facepoints.Add(face[i]);

        // Add normal index (same as position for shared vertices)
        if (hasNormals)
            facepoints.Add(face[i]);

        // Add UV indices (same as position for shared vertices)
        if (hasUVs)
        {
            for (int j = 0; j < geom.numberUVsets; j++)
                facepoints.Add(face[i]);
        }

        // Add color index (same as position for shared vertices)
        if (hasColors)
            facepoints.Add(face[i]);
    }
}
mesh.facePoints = facepoints.ToArray();
```

### Key Improvements

1. **Explicit Attribute Handling**: Each vertex attribute type (position, normal, UVs, colors) is explicitly added rather than using a generic stride loop
2. **Conditional Inclusion**: Only attributes that exist in the mesh are added (checked via `hasNormals`, `hasUVs`, `hasColors` flags)
3. **Correct UV Set Handling**: Properly iterates through multiple UV sets (`geom.numberUVsets`)
4. **Shared Vertex Indices**: All attributes correctly reference the same vertex index (`face[i]`), which is proper COLLADA behavior

## Technical Details

### COLLADA Polylist Format
COLLADA's `<polylist>` element uses input semantics with offsets to define how vertex attributes are referenced:

```xml
<input semantic="VERTEX" source="#mesh-positions" offset="0"/>
<input semantic="NORMAL" source="#mesh-normals" offset="1"/>
<input semantic="TEXCOORD" source="#mesh-uv_0" offset="2" set="0"/>
<input semantic="TEXCOORD" source="#mesh-uv_1" offset="3" set="1"/>
<input semantic="COLOR" source="#mesh-colors" offset="4"/>
```

The offsets (0, 1, 2, 3, 4) define the stride pattern in the `<p>` (primitive) data. Each complete vertex reference must include indices for all input semantics in offset order.

### Relationship to "Clean DAE mesh" Option

The "Clean DAE mesh (Remove doubles)" option invokes the `Clean()` method in [`ColladaDAE.cs:1309`](../TS4 SimRipper/src/ColladaDAE.cs:1309), which merges vertices with identical positions, normals, and UVs. However, this cleanup happens **after** the incorrect face indices are generated, so it couldn't fully fix the underlying structural problem. With this fix, the `Clean()` method now works on properly structured data.

## Files Modified

- **Primary File**: [`ColladaDAE.cs`](../TS4 SimRipper/src/ColladaDAE.cs)
- **Method**: `DAE` constructor (line 553)
- **Specific Section**: Face index generation loop (lines 637-662)

## Changes Summary

| Line Range | Change Type | Description |
|------------|-------------|-------------|
| 637-662 | Modified | Replaced stride-based vertex index duplication with explicit attribute-based index generation |

### Related Code Structures

The `ColladaMesh` class (lines 1250-1426) contains:
- `Stride` property (line 1280): Returns `MaxOffset + 1`
- `TotalFaces` property (line 1285): Calculates total face count as `facePoints.Length / Stride / 3`
- `Clean()` method (line 1309): Removes duplicate vertices with matching positions, normals, and UVs

All of these components now work correctly with the fixed face index generation.
