# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Emby Server is a personal media server with a REST-based API and client libraries. Licensed under GPLv2. Current version: 3.5.3.

## Build System

This is a .NET Framework 4.7 solution using MSBuild with Visual Studio project files (.csproj) and NuGet via `packages.config`. Open `MediaBrowser.sln` in Visual Studio 2015+.

**Build:** `msbuild MediaBrowser.sln` (or build from Visual Studio)

**Tests:** MSTest framework in `MediaBrowser.Tests`. Run from Visual Studio Test Explorer or `vstest.console.exe`.

**Configurations:** Debug, Release, Release Mono, Signed

**NuGet packages** are managed via `packages.config` files per project, restored to the `/packages` directory.

## Architecture

### Layered Structure

1. **API Layer** (`MediaBrowser.Api`) - REST services implementing `IService` from ServiceStack, organized by domain (Library, LiveTv, Session, Users, Movies, Music, etc.). Base class: `BaseApiService`.

2. **Server Implementation** (`Emby.Server.Implementations`) - Core business logic and composition root. `ApplicationHost.cs` is the central DI container using SimpleInjector, wiring up all subsystems.

3. **Data Layer** - SQLite repositories via SQLitePCL.pretty for Items, Users, DisplayPreferences, UserData.

4. **Entry Points:**
   - `MediaBrowser.ServerApplication` - Windows Forms entry point (`MainStartup.cs`)
   - `MediaBrowser.Server.Mono` - Mono/Linux entry point

### Key Subsystems

- **Image Processing** - Three backend implementations behind a common abstraction:
  - `Emby.Drawing.Net` (GDI+)
  - `Emby.Drawing.Skia` (SkiaSharp)
  - `Emby.Drawing.ImageMagick`

- **DLNA/UPnP** (`Emby.Dlna`) - Full protocol stack for device discovery and media serving

- **Metadata Providers** (`MediaBrowser.Providers`, `MediaBrowser.LocalMetadata`, `MediaBrowser.XbmcMetadata`) - Extensible provider pattern for fetching/storing media metadata

- **Media Analysis** - `BDInfo` (Blu-ray), `DvdLib` (DVD/ISO 9660/UDF)

- **Networking** - `SocketHttpListener` (HTTP server), `RSSDP` (service discovery), `Mono.Nat` (NAT traversal)

- **Web UI** (`MediaBrowser.WebDashboard`) - Web-based administration dashboard

### Core NuGet Dependencies

- `MediaBrowser.Common` / `MediaBrowser.Server.Core` (3.5.0) - Shared interfaces (`MediaBrowser.Controller`, `MediaBrowser.Model`)
- `ServiceStack.Text` (5.1.0) - JSON serialization and HTTP services
- `SimpleInjector` (4.3.0) - Dependency injection
- `SkiaSharp` (1.60.0) - Cross-platform image processing
- `SQLitePCL.pretty` (1.1.0) - SQLite data access

### Shared Version

`SharedVersion.cs` at the repository root contains the assembly version used by all projects.

## Windows Service Support

The server application supports running as a Windows service via command-line flags: `-installservice`, `-uninstallservice`, `-service`.
