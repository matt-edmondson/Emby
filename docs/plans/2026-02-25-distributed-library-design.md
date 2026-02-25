# Distributed Library Feature Design

## Summary

Enable an Emby server to connect to other Emby server instances, combine their libraries into a unified catalog, and allow direct play or proxied streaming of remote content.

## Requirements

- **Manual configuration:** Admin enters remote server URL and API key to establish a one-way connection
- **Per-direction sharing:** Connecting to a remote server does not imply the reverse; each direction is configured explicitly
- **Browse + play (v1 scope):** Remote items are browsable and playable; no watch state sync, playlist sharing, or metadata editing
- **Streaming modes:** Direct play (client connects to remote server) or proxy (local server relays stream), configurable per connection
- **Library presentation:** Per-user choice of merged view (remote items alongside local) or separate view (remote libraries as distinct virtual folders)

## Approach: API Client Facade

The local server acts as a REST API client to remote Emby servers. No protocol design needed — remote servers require zero modifications since they already expose the standard Emby REST API.

A sync manager periodically fetches remote library metadata and caches it locally as virtual items in the existing SQLite database. The standard item query pipeline serves these items transparently.

## Architecture

### Connection & Configuration

A new named configuration section `RemoteServers` stores remote connections:

- **Id** (GUID, auto-generated)
- **Name** (display name, e.g., "John's Server")
- **Url** (base URL, e.g., "https://192.168.1.50:8096")
- **ApiKey** (remote server API key for authentication)
- **SyncIntervalMinutes** (metadata refresh interval, default 30)
- **IsEnabled** (toggle without deleting)
- **LibraryIds** (optional filter for which remote libraries to include)
- **StreamingMode** (Direct, Proxy, or Auto)

Implemented via `IConfigurationFactory` pattern. Admin-only CRUD API at `/RemoteServers`.

### Metadata Sync

A `RemoteLibrarySyncManager` scheduled task:

1. Iterates enabled remote server connections
2. Fetches remote library structure via `/Library/VirtualFolders`
3. Fetches items via `/Items` with pagination
4. Maps remote `BaseItemDto` to local `BaseItem` entries:
   - `IsVirtualItem = true`
   - `ExternalServiceId` = remote server connection ID
   - `ExternalId` = remote item's original ID
5. Saves/updates virtual items into `SqliteItemRepository`
6. Removes items no longer present on the remote server

Remote items participate in the normal `LibraryManager.GetItemsResult()` query pipeline with no changes to the core query engine.

### Playback & Streaming

A `RemoteMediaSourceProvider` (implementing `IMediaSourceProvider`) resolves playback for remote items:

- **Direct mode:** Returns `MediaSourceInfo` with remote server's stream URL. Client connects directly to remote server.
- **Proxy mode:** Local server streams from remote via `IHttpClient` and relays to client through a `/RemoteStream/{RemoteServerId}/{ItemId}` endpoint.
- **Auto mode:** Attempts direct, falls back to proxy.

Transcoding is handled by the remote server (which has access to files and FFmpeg). The local server does not transcode remote content.

### Library Presentation

Per-user display preference for remote content:

- **Merged:** Remote items appear alongside local items in standard queries, optionally badged as remote by clients.
- **Separate:** Remote libraries appear as virtual folders named after the remote server, grouped via `TopParentId`.

Admin-level access control restricts which users can see which remote connections.

## New Components

| Component | Location | Purpose |
|---|---|---|
| `RemoteServerConfiguration` | `Emby.Server.Implementations/RemoteLibrary/` | Config model |
| `RemoteServerConfigurationFactory` | `Emby.Server.Implementations/RemoteLibrary/` | Named config factory |
| `RemoteServerService` | `MediaBrowser.Api/RemoteLibrary/` | Admin CRUD API |
| `RemoteLibrarySyncManager` | `Emby.Server.Implementations/RemoteLibrary/` | Scheduled sync task |
| `RemoteMediaSourceProvider` | `Emby.Server.Implementations/RemoteLibrary/` | Playback source resolution |
| `RemoteStreamService` | `MediaBrowser.Api/RemoteLibrary/` | Proxy streaming endpoint |

## Integration Points

- **ApplicationHost.cs** — register new services and manager
- **User configuration** — add remote display mode preference
- **No changes to:** core query engine, DtoService, existing API endpoints, database schema

## Out of Scope (v1)

- Watch state / resume position sync
- Playlist and collection sharing across servers
- Remote metadata editing
- Auto-discovery (LAN broadcast, mDNS)
- Bidirectional sharing negotiation
- Transcoding on behalf of remote servers
