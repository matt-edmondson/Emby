using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
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
						remoteItemIds.Add(remoteItem.Id.ToString("N"));
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
			if (dto.Id == Guid.Empty || string.IsNullOrEmpty(dto.Type))
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

			var remoteItemId = dto.Id.ToString("N");
			var combinedId = serverInfo.Id + "_" + remoteItemId;
			item.Id = combinedId.GetMD5();
			item.Name = dto.Name;
			item.Overview = dto.Overview;
			item.ProductionYear = dto.ProductionYear;
			item.OfficialRating = dto.OfficialRating;
			item.CommunityRating = dto.CommunityRating;
			item.ExternalId = remoteItemId;
			item.IsVirtualItem = true;

			item.SetProviderId("RemoteServerId", serverInfo.Id);
			item.SetProviderId("RemoteItemId", remoteItemId);

			return item;
		}

		private void CleanupRemovedItems(string serverId, HashSet<string> currentRemoteIds, CancellationToken cancellationToken)
		{
			var query = new InternalItemsQuery
			{
				IsVirtualItem = true,
				HasAnyProviderId = new Dictionary<string, string>
				{
					{ "RemoteServerId", serverId }
				}
			};

			var existingItems = _itemRepo.GetItemList(query);

			foreach (var item in existingItems)
			{
				var remoteId = item.GetProviderId("RemoteItemId");
				if (!string.IsNullOrEmpty(remoteId) && !currentRemoteIds.Contains(remoteId))
				{
					_libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = false });
				}
			}
		}
	}
}
