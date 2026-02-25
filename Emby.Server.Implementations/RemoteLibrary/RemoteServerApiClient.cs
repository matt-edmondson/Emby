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
