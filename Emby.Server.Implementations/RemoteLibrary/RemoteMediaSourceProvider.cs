using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
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
