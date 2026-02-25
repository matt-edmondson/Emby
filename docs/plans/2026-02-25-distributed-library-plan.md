# Distributed Library Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable an Emby server to connect to remote Emby instances, sync their library metadata locally, and stream content directly or via proxy.

**Architecture:** API Client Facade — the local server calls remote Emby REST APIs using `IHttpClient`, caches metadata as virtual items (`IsVirtualItem=true`) in the existing SQLite repository, and provides playback via `IMediaSourceProvider`. No changes to remote servers required.

**Tech Stack:** .NET Framework 4.7, ServiceStack REST, SQLitePCL.pretty, SimpleInjector DI

---

### Task 1: Configuration Model

**Files:**
- Create: `Emby.Server.Implementations/RemoteLibrary/RemoteServerInfo.cs`
- Create: `Emby.Server.Implementations/RemoteLibrary/RemoteLibraryOptions.cs`
- Modify: `Emby.Server.Implementations/Emby.Server.Implementations.csproj` (add Compile entries)

**Step 1: Create the RemoteServerInfo model**

Create `Emby.Server.Implementations/RemoteLibrary/RemoteServerInfo.cs`:

```csharp
using System;

namespace Emby.Server.Implementations.RemoteLibrary
{
	public enum StreamingMode
	{
		Direct,
		Proxy,
		Auto
	}

	public class RemoteServerInfo
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Url { get; set; }
		public string ApiKey { get; set; }
		public int SyncIntervalMinutes { get; set; }
		public bool IsEnabled { get; set; }
		public string[] LibraryIds { get; set; }
		public StreamingMode StreamingMode { get; set; }

		public RemoteServerInfo()
		{
			Id = Guid.NewGuid().ToString("N");
			SyncIntervalMinutes = 30;
			IsEnabled = true;
			LibraryIds = Array.Empty<string>();
			StreamingMode = StreamingMode.Direct;
		}
	}
}
```

**Step 2: Create the RemoteLibraryOptions container**

Create `Emby.Server.Implementations/RemoteLibrary/RemoteLibraryOptions.cs`:

```csharp
using System;

namespace Emby.Server.Implementations.RemoteLibrary
{
	public class RemoteLibraryOptions
	{
		public RemoteServerInfo[] Servers { get; set; }

		public RemoteLibraryOptions()
		{
			Servers = Array.Empty<RemoteServerInfo>();
		}
	}
}
```

**Step 3: Add Compile entries to .csproj**

In `Emby.Server.Implementations/Emby.Server.Implementations.csproj`, add inside the `<ItemGroup>` that contains other `<Compile>` entries (after the Branding entries around line 49):

```xml
<Compile Include="RemoteLibrary\RemoteServerInfo.cs" />
<Compile Include="RemoteLibrary\RemoteLibraryOptions.cs" />
```

**Step 4: Build to verify**

Run: `msbuild Emby.Server.Implementations/Emby.Server.Implementations.csproj /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors

**Step 5: Commit**

```bash
git add Emby.Server.Implementations/RemoteLibrary/RemoteServerInfo.cs Emby.Server.Implementations/RemoteLibrary/RemoteLibraryOptions.cs Emby.Server.Implementations/Emby.Server.Implementations.csproj
git commit -m "feat: add remote server configuration models"
```

---

### Task 2: Configuration Factory

**Files:**
- Create: `Emby.Server.Implementations/RemoteLibrary/RemoteLibraryConfigurationFactory.cs`
- Modify: `Emby.Server.Implementations/Emby.Server.Implementations.csproj` (add Compile entry)

**Context:** Follow the exact pattern from `Emby.Server.Implementations/Branding/BrandingConfigurationFactory.cs`. The framework auto-discovers `IConfigurationFactory` implementations via `GetExports<IConfigurationFactory>()` in `ApplicationHost.FindParts()` — no manual registration needed.

**Step 1: Create the configuration factory**

Create `Emby.Server.Implementations/RemoteLibrary/RemoteLibraryConfigurationFactory.cs`:

```csharp
using MediaBrowser.Common.Configuration;
using System.Collections.Generic;

namespace Emby.Server.Implementations.RemoteLibrary
{
	public class RemoteLibraryConfigurationFactory : IConfigurationFactory
	{
		public IEnumerable<ConfigurationStore> GetConfigurations()
		{
			return new[]
			{
				new ConfigurationStore
				{
					ConfigurationType = typeof(RemoteLibraryOptions),
					Key = "remoteservers"
				}
			};
		}
	}
}
```

**Step 2: Add Compile entry to .csproj**

In `Emby.Server.Implementations/Emby.Server.Implementations.csproj`, add alongside the other RemoteLibrary entries:

```xml
<Compile Include="RemoteLibrary\RemoteLibraryConfigurationFactory.cs" />
```

**Step 3: Build to verify**

Run: `msbuild Emby.Server.Implementations/Emby.Server.Implementations.csproj /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors

**Step 4: Commit**

```bash
git add Emby.Server.Implementations/RemoteLibrary/RemoteLibraryConfigurationFactory.cs Emby.Server.Implementations/Emby.Server.Implementations.csproj
git commit -m "feat: add remote library configuration factory"
```

---

### Task 3: Remote Server API Client

**Files:**
- Create: `Emby.Server.Implementations/RemoteLibrary/RemoteServerApiClient.cs`
- Modify: `Emby.Server.Implementations/Emby.Server.Implementations.csproj` (add Compile entry)

**Context:** This class wraps `IHttpClient` (see `Emby.Server.Implementations/HttpClientManager/HttpClientManager.cs`) to call remote Emby REST APIs. Uses `HttpRequestOptions` with `Url` property and `SendAsync()`. Responses come back as `HttpResponseInfo` with a `Content` stream. Deserialize with `IJsonSerializer`.

**Step 1: Create the API client**

Create `Emby.Server.Implementations/RemoteLibrary/RemoteServerApiClient.cs`:

```csharp
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Server.Implementations.RemoteLibrary
{
	public class RemoteServerApiClient
	{
		private readonly IHttpClient _httpClient;
		private readonly IJsonSerializer _json;
		private readonly ILogger _logger;
		private readonly RemoteServerInfo _serverInfo;

		public RemoteServerApiClient(IHttpClient httpClient, IJsonSerializer json, ILogger logger, RemoteServerInfo serverInfo)
		{
			_httpClient = httpClient;
			_json = json;
			_logger = logger;
			_serverInfo = serverInfo;
		}

		private string BuildUrl(string path)
		{
			var baseUrl = _serverInfo.Url.TrimEnd('/');
			return baseUrl + path + (path.Contains("?") ? "&" : "?") + "api_key=" + _serverInfo.ApiKey;
		}

		public async Task<QueryResult<BaseItemDto>> GetItems(int startIndex, int limit, CancellationToken cancellationToken)
		{
			var url = BuildUrl("/Items?Recursive=true&StartIndex=" + startIndex + "&Limit=" + limit + "&Fields=Path,Overview,Genres,Studios,People,MediaSources,ProviderIds");
			return await GetJsonResponse<QueryResult<BaseItemDto>>(url, cancellationToken).ConfigureAwait(false);
		}

		public async Task<BaseItemDto[]> GetVirtualFolders(CancellationToken cancellationToken)
		{
			var url = BuildUrl("/Library/VirtualFolders");
			return await GetJsonResponse<BaseItemDto[]>(url, cancellationToken).ConfigureAwait(false);
		}

		public string GetStreamUrl(string itemId)
		{
			return BuildUrl("/Videos/" + itemId + "/stream");
		}

		public async Task<Stream> GetStream(string itemId, CancellationToken cancellationToken)
		{
			var url = BuildUrl("/Videos/" + itemId + "/stream");
			var options = new HttpRequestOptions
			{
				Url = url,
				CancellationToken = cancellationToken,
				BufferContent = false
			};
			return await _httpClient.Get(options).ConfigureAwait(false);
		}

		public async Task<bool> TestConnection(CancellationToken cancellationToken)
		{
			try
			{
				var url = BuildUrl("/System/Info/Public");
				await GetJsonResponse<object>(url, cancellationToken).ConfigureAwait(false);
				return true;
			}
			catch (Exception ex)
			{
				_logger.ErrorException("Failed to connect to remote server {0}", ex, _serverInfo.Url);
				return false;
			}
		}

		private async Task<T> GetJsonResponse<T>(string url, CancellationToken cancellationToken)
		{
			var options = new HttpRequestOptions
			{
				Url = url,
				CancellationToken = cancellationToken,
				BufferContent = false
			};

			using (var response = await _httpClient.SendAsync(options, "GET").ConfigureAwait(false))
			{
				using (var stream = response.Content)
				{
					return _json.DeserializeFromStream<T>(stream);
				}
			}
		}
	}
}
```

**Step 2: Add Compile entry to .csproj**

```xml
<Compile Include="RemoteLibrary\RemoteServerApiClient.cs" />
```

**Step 3: Build to verify**

Run: `msbuild Emby.Server.Implementations/Emby.Server.Implementations.csproj /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors

**Step 4: Commit**

```bash
git add Emby.Server.Implementations/RemoteLibrary/RemoteServerApiClient.cs Emby.Server.Implementations/Emby.Server.Implementations.csproj
git commit -m "feat: add remote server API client"
```

---

### Task 4: Remote Library Sync Scheduled Task

**Files:**
- Create: `Emby.Server.Implementations/RemoteLibrary/RemoteLibrarySyncTask.cs`
- Modify: `Emby.Server.Implementations/Emby.Server.Implementations.csproj` (add Compile entry)

**Context:** Follow the pattern from `Emby.Server.Implementations/ScheduledTasks/RefreshMediaLibraryTask.cs`. Implements `IScheduledTask` with `Execute(CancellationToken, IProgress<double>)`. The framework auto-discovers implementations via `GetExports<IScheduledTask>()`. Access configuration via `IConfigurationManager.GetConfiguration<RemoteLibraryOptions>("remoteservers")`. Use `ILibraryManager` to save items.

**Step 1: Create the sync task**

Create `Emby.Server.Implementations/RemoteLibrary/RemoteLibrarySyncTask.cs`:

```csharp
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Server.Implementations.RemoteLibrary
{
	public class RemoteLibrarySyncTask : IScheduledTask
	{
		private readonly IConfigurationManager _config;
		private readonly IHttpClient _httpClient;
		private readonly IJsonSerializer _json;
		private readonly ILibraryManager _libraryManager;
		private readonly IItemRepository _itemRepo;
		private readonly ILogger _logger;

		public RemoteLibrarySyncTask(
			IConfigurationManager config,
			IHttpClient httpClient,
			IJsonSerializer json,
			ILibraryManager libraryManager,
			IItemRepository itemRepo,
			ILogManager logManager)
		{
			_config = config;
			_httpClient = httpClient;
			_json = json;
			_libraryManager = libraryManager;
			_itemRepo = itemRepo;
			_logger = logManager.GetLogger(GetType().Name);
		}

		public string Name { get { return "Sync remote libraries"; } }
		public string Description { get { return "Fetches library metadata from connected remote Emby servers."; } }
		public string Category { get { return "Library"; } }
		public string Key { get { return "SyncRemoteLibraries"; } }

		public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
		{
			return new[]
			{
				new TaskTriggerInfo
				{
					Type = TaskTriggerInfo.TriggerStartup
				},
				new TaskTriggerInfo
				{
					Type = TaskTriggerInfo.TriggerInterval,
					IntervalTicks = TimeSpan.FromMinutes(30).Ticks
				}
			};
		}

		public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
		{
			var options = _config.GetConfiguration<RemoteLibraryOptions>("remoteservers");
			var servers = options.Servers.Where(s => s.IsEnabled).ToArray();

			if (servers.Length == 0)
			{
				progress.Report(100);
				return;
			}

			for (var i = 0; i < servers.Length; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var server = servers[i];
				var progressBase = (double)i / servers.Length * 100;

				try
				{
					await SyncServer(server, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					_logger.ErrorException("Error syncing remote server {0}", ex, server.Name);
				}

				progress.Report(progressBase + (100.0 / servers.Length));
			}

			progress.Report(100);
		}

		private async Task SyncServer(RemoteServerInfo serverInfo, CancellationToken cancellationToken)
		{
			var client = new RemoteServerApiClient(_httpClient, _json, _logger, serverInfo);

			if (!await client.TestConnection(cancellationToken).ConfigureAwait(false))
			{
				_logger.Warn("Skipping sync for unreachable server: {0}", serverInfo.Name);
				return;
			}

			_logger.Info("Starting sync for remote server: {0}", serverInfo.Name);

			var remoteItemIds = new HashSet<string>();
			var startIndex = 0;
			var pageSize = 100;

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var result = await client.GetItems(startIndex, pageSize, cancellationToken).ConfigureAwait(false);

				if (result.Items == null || result.Items.Length == 0)
				{
					break;
				}

				foreach (var remoteItem in result.Items)
				{
					var localItem = MapRemoteItem(remoteItem, serverInfo);
					if (localItem != null)
					{
						_itemRepo.SaveItem(localItem, cancellationToken);
						remoteItemIds.Add(remoteItem.Id);
					}
				}

				startIndex += result.Items.Length;

				if (startIndex >= result.TotalRecordCount)
				{
					break;
				}
			}

			CleanupRemovedItems(serverInfo.Id, remoteItemIds, cancellationToken);

			_logger.Info("Completed sync for remote server: {0}. Items synced: {1}", serverInfo.Name, remoteItemIds.Count);
		}

		private BaseItem MapRemoteItem(BaseItemDto dto, RemoteServerInfo serverInfo)
		{
			if (string.IsNullOrEmpty(dto.Id) || string.IsNullOrEmpty(dto.Type))
			{
				return null;
			}

			BaseItem item;

			switch (dto.Type)
			{
				case "Movie":
					item = new Movie();
					break;
				case "Series":
					item = new Series();
					break;
				case "Season":
					item = new Season();
					break;
				case "Episode":
					item = new Episode();
					break;
				case "Audio":
					item = new MediaBrowser.Controller.Entities.Audio.Audio();
					break;
				case "MusicAlbum":
					item = new MediaBrowser.Controller.Entities.Audio.MusicAlbum();
					break;
				default:
					item = new Video();
					break;
			}

			// Use a deterministic GUID based on server ID + remote item ID
			var combinedId = serverInfo.Id + "_" + dto.Id;
			item.Id = combinedId.GetMD5();
			item.Name = dto.Name;
			item.Overview = dto.Overview;
			item.ProductionYear = dto.ProductionYear;
			item.OfficialRating = dto.OfficialRating;
			item.CommunityRating = dto.CommunityRating;
			item.ExternalId = dto.Id;
			item.IsVirtualItem = true;

			// Tag with server connection info for playback resolution
			item.SetProviderId("RemoteServerId", serverInfo.Id);
			item.SetProviderId("RemoteItemId", dto.Id);

			return item;
		}

		private void CleanupRemovedItems(string serverId, HashSet<string> currentRemoteIds, CancellationToken cancellationToken)
		{
			var query = new InternalItemsQuery
			{
				IsVirtualItem = true,
				HasAnyProviderId = new[] { "RemoteServerId" }
			};

			var existingItems = _itemRepo.GetItemList(query);

			foreach (var item in existingItems)
			{
				if (item.GetProviderId("RemoteServerId") != serverId)
				{
					continue;
				}

				var remoteId = item.GetProviderId("RemoteItemId");
				if (!string.IsNullOrEmpty(remoteId) && !currentRemoteIds.Contains(remoteId))
				{
					_libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = false });
				}
			}
		}
	}
}
```

**Step 2: Add Compile entry to .csproj**

```xml
<Compile Include="RemoteLibrary\RemoteLibrarySyncTask.cs" />
```

**Step 3: Build to verify**

Run: `msbuild Emby.Server.Implementations/Emby.Server.Implementations.csproj /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors. Some warnings may appear for unused usings — acceptable.

**Step 4: Commit**

```bash
git add Emby.Server.Implementations/RemoteLibrary/RemoteLibrarySyncTask.cs Emby.Server.Implementations/Emby.Server.Implementations.csproj
git commit -m "feat: add remote library sync scheduled task"
```

---

### Task 5: Remote Media Source Provider

**Files:**
- Create: `Emby.Server.Implementations/RemoteLibrary/RemoteMediaSourceProvider.cs`
- Modify: `Emby.Server.Implementations/Emby.Server.Implementations.csproj` (add Compile entry)

**Context:** Follow the pattern from `Emby.Server.Implementations/Channels/ChannelDynamicMediaSourceProvider.cs`. Implements `IMediaSourceProvider` with `GetMediaSources(BaseItem, CancellationToken)` and `OpenMediaSource(string, List<ILiveStream>, CancellationToken)`. Auto-discovered via `MediaSourceManager.AddParts(GetExports<IMediaSourceProvider>())` in `ApplicationHost.cs` line 1399.

**Step 1: Create the media source provider**

Create `Emby.Server.Implementations/RemoteLibrary/RemoteMediaSourceProvider.cs`:

```csharp
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Server.Implementations.RemoteLibrary
{
	public class RemoteMediaSourceProvider : IMediaSourceProvider
	{
		private readonly IConfigurationManager _config;
		private readonly IHttpClient _httpClient;
		private readonly IJsonSerializer _json;
		private readonly ILogger _logger;

		public RemoteMediaSourceProvider(
			IConfigurationManager config,
			IHttpClient httpClient,
			IJsonSerializer json,
			ILogManager logManager)
		{
			_config = config;
			_httpClient = httpClient;
			_json = json;
			_logger = logManager.GetLogger(GetType().Name);
		}

		public Task<IEnumerable<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken cancellationToken)
		{
			var serverId = item.GetProviderId("RemoteServerId");
			var remoteItemId = item.GetProviderId("RemoteItemId");

			if (string.IsNullOrEmpty(serverId) || string.IsNullOrEmpty(remoteItemId))
			{
				return Task.FromResult<IEnumerable<MediaSourceInfo>>(Array.Empty<MediaSourceInfo>());
			}

			var options = _config.GetConfiguration<RemoteLibraryOptions>("remoteservers");
			var server = options.Servers.FirstOrDefault(s => s.Id == serverId && s.IsEnabled);

			if (server == null)
			{
				return Task.FromResult<IEnumerable<MediaSourceInfo>>(Array.Empty<MediaSourceInfo>());
			}

			var client = new RemoteServerApiClient(_httpClient, _json, _logger, server);

			var sources = new List<MediaSourceInfo>();

			if (server.StreamingMode == StreamingMode.Direct || server.StreamingMode == StreamingMode.Auto)
			{
				sources.Add(new MediaSourceInfo
				{
					Id = "remote_direct_" + remoteItemId,
					Path = client.GetStreamUrl(remoteItemId),
					Protocol = MediaProtocol.Http,
					Name = "Remote (Direct)",
					Type = MediaSourceType.Default,
					SupportsDirectStream = true,
					IsRemote = true
				});
			}

			if (server.StreamingMode == StreamingMode.Proxy || server.StreamingMode == StreamingMode.Auto)
			{
				sources.Add(new MediaSourceInfo
				{
					Id = "remote_proxy_" + remoteItemId,
					Path = "/RemoteStream/" + serverId + "/" + remoteItemId,
					Protocol = MediaProtocol.Http,
					Name = "Remote (Proxy)",
					Type = MediaSourceType.Default,
					SupportsDirectStream = true,
					IsRemote = true
				});
			}

			return Task.FromResult<IEnumerable<MediaSourceInfo>>(sources);
		}

		public Task<ILiveStream> OpenMediaSource(string openToken, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
		{
			throw new NotImplementedException();
		}
	}
}
```

**Step 2: Add Compile entry to .csproj**

```xml
<Compile Include="RemoteLibrary\RemoteMediaSourceProvider.cs" />
```

**Step 3: Build to verify**

Run: `msbuild Emby.Server.Implementations/Emby.Server.Implementations.csproj /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors

**Step 4: Commit**

```bash
git add Emby.Server.Implementations/RemoteLibrary/RemoteMediaSourceProvider.cs Emby.Server.Implementations/Emby.Server.Implementations.csproj
git commit -m "feat: add remote media source provider for playback"
```

---

### Task 6: Admin API Service for Remote Server Management

**Files:**
- Create: `MediaBrowser.Api/RemoteLibrary/RemoteServerService.cs`
- Modify: `MediaBrowser.Api/MediaBrowser.Api.csproj` (add Compile entry)

**Context:** Follow the pattern from `MediaBrowser.Api/ConfigurationService.cs`. Services extend `BaseApiService`, use `[Route]` attributes and `[Authenticated(Roles = "Admin")]`. Inject `IConfigurationManager` for config reads, `IJsonSerializer` for deserialization. ServiceStack auto-discovers service classes.

**Step 1: Create the API service**

Create `MediaBrowser.Api/RemoteLibrary/RemoteServerService.cs`:

```csharp
using Emby.Server.Implementations.RemoteLibrary;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using ServiceStack;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Api.RemoteLibrary
{
	[Route("/RemoteServers", "GET", Summary = "Gets all configured remote servers")]
	[Authenticated(Roles = "Admin")]
	public class GetRemoteServers : IReturn<RemoteServerInfo[]>
	{
	}

	[Route("/RemoteServers", "POST", Summary = "Adds or updates a remote server connection")]
	[Authenticated(Roles = "Admin")]
	public class UpdateRemoteServer : RemoteServerInfo, IReturnVoid
	{
	}

	[Route("/RemoteServers/{Id}", "DELETE", Summary = "Removes a remote server connection")]
	[Authenticated(Roles = "Admin")]
	public class DeleteRemoteServer : IReturnVoid
	{
		[ApiMember(Name = "Id", Description = "The remote server ID", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
		public string Id { get; set; }
	}

	[Route("/RemoteServers/{Id}/Test", "POST", Summary = "Tests connectivity to a remote server")]
	[Authenticated(Roles = "Admin")]
	public class TestRemoteServer : IReturn<RemoteServerTestResult>
	{
		[ApiMember(Name = "Id", Description = "The remote server ID", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
		public string Id { get; set; }
	}

	public class RemoteServerTestResult
	{
		public bool IsSuccess { get; set; }
		public string Message { get; set; }
	}

	public class RemoteServerService : BaseApiService
	{
		private readonly IConfigurationManager _config;
		private readonly IHttpClient _httpClient;
		private readonly IJsonSerializer _json;
		private readonly ILogger _logger;

		public RemoteServerService(IConfigurationManager config, IHttpClient httpClient, IJsonSerializer json, ILogManager logManager)
		{
			_config = config;
			_httpClient = httpClient;
			_json = json;
			_logger = logManager.GetLogger(GetType().Name);
		}

		public object Get(GetRemoteServers request)
		{
			var options = _config.GetConfiguration<RemoteLibraryOptions>("remoteservers");
			return ToOptimizedResult(options.Servers);
		}

		public void Post(UpdateRemoteServer request)
		{
			var options = _config.GetConfiguration<RemoteLibraryOptions>("remoteservers");
			var servers = options.Servers.ToList();

			var existing = servers.FirstOrDefault(s => string.Equals(s.Id, request.Id, StringComparison.OrdinalIgnoreCase));

			if (existing != null)
			{
				existing.Name = request.Name;
				existing.Url = request.Url;
				existing.ApiKey = request.ApiKey;
				existing.SyncIntervalMinutes = request.SyncIntervalMinutes;
				existing.IsEnabled = request.IsEnabled;
				existing.LibraryIds = request.LibraryIds;
				existing.StreamingMode = request.StreamingMode;
			}
			else
			{
				if (string.IsNullOrEmpty(request.Id))
				{
					request.Id = Guid.NewGuid().ToString("N");
				}
				servers.Add(new RemoteServerInfo
				{
					Id = request.Id,
					Name = request.Name,
					Url = request.Url,
					ApiKey = request.ApiKey,
					SyncIntervalMinutes = request.SyncIntervalMinutes,
					IsEnabled = request.IsEnabled,
					LibraryIds = request.LibraryIds ?? Array.Empty<string>(),
					StreamingMode = request.StreamingMode
				});
			}

			options.Servers = servers.ToArray();
			_config.SaveConfiguration("remoteservers", options);
		}

		public void Delete(DeleteRemoteServer request)
		{
			var options = _config.GetConfiguration<RemoteLibraryOptions>("remoteservers");
			options.Servers = options.Servers
				.Where(s => !string.Equals(s.Id, request.Id, StringComparison.OrdinalIgnoreCase))
				.ToArray();
			_config.SaveConfiguration("remoteservers", options);
		}

		public async Task<object> Post(TestRemoteServer request)
		{
			var options = _config.GetConfiguration<RemoteLibraryOptions>("remoteservers");
			var server = options.Servers.FirstOrDefault(s => string.Equals(s.Id, request.Id, StringComparison.OrdinalIgnoreCase));

			if (server == null)
			{
				return ToOptimizedResult(new RemoteServerTestResult
				{
					IsSuccess = false,
					Message = "Remote server not found."
				});
			}

			var client = new RemoteServerApiClient(_httpClient, _json, _logger, server);
			var success = await client.TestConnection(CancellationToken.None).ConfigureAwait(false);

			return ToOptimizedResult(new RemoteServerTestResult
			{
				IsSuccess = success,
				Message = success ? "Connection successful." : "Could not connect to remote server."
			});
		}
	}
}
```

**Step 2: Add Compile entry to MediaBrowser.Api.csproj**

In `MediaBrowser.Api/MediaBrowser.Api.csproj`, add inside the `<Compile>` ItemGroup:

```xml
<Compile Include="RemoteLibrary\RemoteServerService.cs" />
```

**Step 3: Build to verify**

Run: `msbuild MediaBrowser.sln /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors

**Step 4: Commit**

```bash
git add MediaBrowser.Api/RemoteLibrary/RemoteServerService.cs MediaBrowser.Api/MediaBrowser.Api.csproj
git commit -m "feat: add admin API for remote server management"
```

---

### Task 7: Proxy Streaming Endpoint

**Files:**
- Create: `MediaBrowser.Api/RemoteLibrary/RemoteStreamService.cs`
- Modify: `MediaBrowser.Api/MediaBrowser.Api.csproj` (add Compile entry)

**Context:** This endpoint proxies a stream from a remote server through the local server. Uses `IHttpClient.Get()` which returns a `Stream`, then wraps it in an HTTP response to the client.

**Step 1: Create the proxy streaming service**

Create `MediaBrowser.Api/RemoteLibrary/RemoteStreamService.cs`:

```csharp
using Emby.Server.Implementations.RemoteLibrary;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using ServiceStack;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Api.RemoteLibrary
{
	[Route("/RemoteStream/{ServerId}/{ItemId}", "GET", Summary = "Proxies a media stream from a remote server")]
	[Authenticated]
	public class GetRemoteStream : IReturn<object>
	{
		[ApiMember(Name = "ServerId", Description = "The remote server ID", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
		public string ServerId { get; set; }

		[ApiMember(Name = "ItemId", Description = "The remote item ID", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
		public string ItemId { get; set; }
	}

	public class RemoteStreamService : BaseApiService
	{
		private readonly IConfigurationManager _config;
		private readonly IHttpClient _httpClient;
		private readonly IJsonSerializer _json;
		private readonly ILogger _logger;

		public RemoteStreamService(IConfigurationManager config, IHttpClient httpClient, IJsonSerializer json, ILogManager logManager)
		{
			_config = config;
			_httpClient = httpClient;
			_json = json;
			_logger = logManager.GetLogger(GetType().Name);
		}

		public async Task<object> Get(GetRemoteStream request)
		{
			var options = _config.GetConfiguration<RemoteLibraryOptions>("remoteservers");
			var server = options.Servers.FirstOrDefault(s =>
				string.Equals(s.Id, request.ServerId, StringComparison.OrdinalIgnoreCase) && s.IsEnabled);

			if (server == null)
			{
				throw new ArgumentException("Remote server not found or disabled.");
			}

			var client = new RemoteServerApiClient(_httpClient, _json, _logger, server);
			var stream = await client.GetStream(request.ItemId, CancellationToken.None).ConfigureAwait(false);

			return ResultFactory.GetResult(stream, "video/mp4", new System.Collections.Generic.Dictionary<string, string>());
		}
	}
}
```

**Step 2: Add Compile entry to MediaBrowser.Api.csproj**

```xml
<Compile Include="RemoteLibrary\RemoteStreamService.cs" />
```

**Step 3: Build to verify**

Run: `msbuild MediaBrowser.sln /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors

**Step 4: Commit**

```bash
git add MediaBrowser.Api/RemoteLibrary/RemoteStreamService.cs MediaBrowser.Api/MediaBrowser.Api.csproj
git commit -m "feat: add proxy streaming endpoint for remote content"
```

---

### Task 8: Integration — Wire Up in ApplicationHost

**Files:**
- Modify: `Emby.Server.Implementations/ApplicationHost.cs`

**Context:** While `IConfigurationFactory`, `IScheduledTask`, and `IMediaSourceProvider` are all auto-discovered by `GetExports<T>()`, verify the project reference exists from `MediaBrowser.Api` to `Emby.Server.Implementations` (it does — needed for the config model types). No explicit registration needed, but confirm the build links everything.

**Step 1: Build the full solution**

Run: `msbuild MediaBrowser.sln /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors

All new components use interfaces that are auto-discovered:
- `RemoteLibraryConfigurationFactory` via `GetExports<IConfigurationFactory>()` at ApplicationHost line ~1365
- `RemoteLibrarySyncTask` via `GetExports<IScheduledTask>()`
- `RemoteMediaSourceProvider` via `GetExports<IMediaSourceProvider>()` at ApplicationHost line ~1399

**Step 2: Verify no explicit registration needed**

Read `ApplicationHost.cs` lines 1363-1405 to confirm auto-discovery. If the above types are public and have constructors with injectable parameters, they will be automatically instantiated by `CreateInstanceSafe`.

**Step 3: Commit (if any changes needed)**

If the full solution build passes without changes to ApplicationHost.cs, no commit is needed for this task.

---

### Task 9: Unit Tests — Configuration Model

**Files:**
- Create: `MediaBrowser.Tests/RemoteLibrary/RemoteServerInfoTests.cs`
- Modify: `MediaBrowser.Tests/MediaBrowser.Tests.csproj` (add Compile entry)

**Step 1: Write tests for the configuration model**

Create `MediaBrowser.Tests/RemoteLibrary/RemoteServerInfoTests.cs`:

```csharp
using Emby.Server.Implementations.RemoteLibrary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace MediaBrowser.Tests.RemoteLibrary
{
	[TestClass]
	public class RemoteServerInfoTests
	{
		[TestMethod]
		public void Constructor_SetsDefaultValues()
		{
			var info = new RemoteServerInfo();

			Assert.IsFalse(string.IsNullOrEmpty(info.Id));
			Assert.AreEqual(30, info.SyncIntervalMinutes);
			Assert.IsTrue(info.IsEnabled);
			Assert.IsNotNull(info.LibraryIds);
			Assert.AreEqual(0, info.LibraryIds.Length);
			Assert.AreEqual(StreamingMode.Direct, info.StreamingMode);
		}

		[TestMethod]
		public void Constructor_GeneratesUniqueIds()
		{
			var info1 = new RemoteServerInfo();
			var info2 = new RemoteServerInfo();

			Assert.AreNotEqual(info1.Id, info2.Id);
		}
	}

	[TestClass]
	public class RemoteLibraryOptionsTests
	{
		[TestMethod]
		public void Constructor_SetsEmptyServersArray()
		{
			var options = new RemoteLibraryOptions();

			Assert.IsNotNull(options.Servers);
			Assert.AreEqual(0, options.Servers.Length);
		}
	}
}
```

**Step 2: Add Compile entry to test .csproj**

In `MediaBrowser.Tests/MediaBrowser.Tests.csproj`, add inside the `<Compile>` ItemGroup:

```xml
<Compile Include="RemoteLibrary\RemoteServerInfoTests.cs" />
```

**Step 3: Build the test project**

Run: `msbuild MediaBrowser.Tests/MediaBrowser.Tests.csproj /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors

**Step 4: Run the tests**

Run: `vstest.console.exe MediaBrowser.Tests/bin/Debug/MediaBrowser.Tests.dll /Tests:Constructor_SetsDefaultValues,Constructor_GeneratesUniqueIds,Constructor_SetsEmptyServersArray`
Expected: 3 tests passed

**Step 5: Commit**

```bash
git add MediaBrowser.Tests/RemoteLibrary/RemoteServerInfoTests.cs MediaBrowser.Tests/MediaBrowser.Tests.csproj
git commit -m "test: add unit tests for remote server configuration models"
```

---

### Task 10: Unit Tests — Remote Server API Client URL Building

**Files:**
- Create: `MediaBrowser.Tests/RemoteLibrary/RemoteServerApiClientTests.cs`
- Modify: `MediaBrowser.Tests/MediaBrowser.Tests.csproj` (add Compile entry)

**Step 1: Write tests for URL building and stream URL generation**

Create `MediaBrowser.Tests/RemoteLibrary/RemoteServerApiClientTests.cs`:

```csharp
using Emby.Server.Implementations.RemoteLibrary;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.RemoteLibrary
{
	[TestClass]
	public class RemoteServerApiClientTests
	{
		[TestMethod]
		public void GetStreamUrl_ReturnsCorrectUrl()
		{
			var serverInfo = new RemoteServerInfo
			{
				Url = "https://192.168.1.50:8096",
				ApiKey = "testkey123"
			};

			var client = new RemoteServerApiClient(null, null, null, serverInfo);
			var url = client.GetStreamUrl("abc123");

			Assert.IsTrue(url.Contains("/Videos/abc123/stream"));
			Assert.IsTrue(url.Contains("api_key=testkey123"));
		}

		[TestMethod]
		public void GetStreamUrl_HandlesTrailingSlash()
		{
			var serverInfo = new RemoteServerInfo
			{
				Url = "https://192.168.1.50:8096/",
				ApiKey = "testkey"
			};

			var client = new RemoteServerApiClient(null, null, null, serverInfo);
			var url = client.GetStreamUrl("item1");

			Assert.IsFalse(url.Contains("//Videos"));
			Assert.IsTrue(url.Contains("/Videos/item1/stream"));
		}
	}
}
```

**Step 2: Add Compile entry to test .csproj**

```xml
<Compile Include="RemoteLibrary\RemoteServerApiClientTests.cs" />
```

**Step 3: Build and run tests**

Run: `msbuild MediaBrowser.Tests/MediaBrowser.Tests.csproj /p:Configuration=Debug /v:minimal`
Expected: Build succeeded

Run: `vstest.console.exe MediaBrowser.Tests/bin/Debug/MediaBrowser.Tests.dll /Tests:GetStreamUrl_ReturnsCorrectUrl,GetStreamUrl_HandlesTrailingSlash`
Expected: 2 tests passed

**Step 4: Commit**

```bash
git add MediaBrowser.Tests/RemoteLibrary/RemoteServerApiClientTests.cs MediaBrowser.Tests/MediaBrowser.Tests.csproj
git commit -m "test: add unit tests for remote server API client"
```

---

### Task 11: Full Solution Build Verification

**Step 1: Clean build**

Run: `msbuild MediaBrowser.sln /t:Clean /p:Configuration=Debug /v:minimal`
Run: `msbuild MediaBrowser.sln /p:Configuration=Debug /v:minimal`
Expected: Build succeeded, 0 errors across all projects

**Step 2: Run all tests**

Run: `vstest.console.exe MediaBrowser.Tests/bin/Debug/MediaBrowser.Tests.dll`
Expected: All tests pass (existing + new)

**Step 3: Commit if any fixes were needed**

If build or test failures required fixes, commit them:
```bash
git commit -am "fix: resolve build issues in distributed library feature"
```
