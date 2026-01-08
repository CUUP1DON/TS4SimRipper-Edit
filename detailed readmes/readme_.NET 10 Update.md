# TS4 SimRipper .NET 10.0 Update - Change Log

## Overview
This document summarizes the changes made to update the TS4 SimRipper program from its original .NET framework to .NET 10.0.

## Changes Made

### 1. Project File Updates
- **File**: [`TS4SimRipper.csproj`](../TS4 SimRipper/src/TS4SimRipper.csproj)
- **File**: [`TS4SimRipper.NET.csproj`](../TS4 SimRipper/src/TS4SimRipper.NET.csproj)
- **Change**: Updated `TargetFramework` from previous version to `net10.0-windows`

### 2. Key Configuration Updates
- **Target Framework**: `net10.0-windows` (previously targeting older .NET versions)
- **Runtime Identifier**: `win-x64` maintained
- **Platform Target**: `x64` maintained
- **Language Version**: `13.0` maintained

### 3. Package Reference Updates
The following NuGet packages were updated to ensure compatibility with .NET 10.0:

| Package | Version | Purpose |
|---------|---------|---------|
| `protobuf-net` | 3.2.30 | Protocol Buffers serialization |
| `Ookii.Dialogs.Wpf` | 5.0.1 | Windows dialogs and common controls |
| `System.IO.Compression` | 4.3.0 | Compression utilities |
| `System.Resources.Extensions` | 10.0.0 | Resource management |

### 5. Build Configuration
- **Debug Configuration**: Portable debug symbols enabled
- **Release Configuration**: Embedded debug symbols
- **Auto-generate binding redirects**: Enabled
- **Self-contained deployment**: Disabled (requires .NET 10.0 runtime)

## Technical Notes
- The project uses Windows Forms and WPF for the UI
- Requires .NET 10.0 Windows Desktop Runtime for execution
- All external DLL dependencies are properly referenced with relative paths
- The build produces a 64-bit Windows executable