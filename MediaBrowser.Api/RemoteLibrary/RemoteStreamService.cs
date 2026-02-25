using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
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
		private const string ConfigKey = "remoteservers";

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

		private RemoteLibraryOptionsDto GetOptions()
		{
			var configObject = _config.GetConfiguration(ConfigKey);
			var json = _json.SerializeToString(configObject);
			return _json.DeserializeFromString<RemoteLibraryOptionsDto>(json);
		}

		public async Task<object> Get(GetRemoteStream request)
		{
			var options = GetOptions();
			var server = options.Servers.FirstOrDefault(s =>
				string.Equals(s.Id, request.ServerId, StringComparison.OrdinalIgnoreCase) && s.IsEnabled);

			if (server == null)
			{
				throw new ArgumentException("Remote server not found or disabled.");
			}

			var baseUrl = server.Url.TrimEnd('/');
			var streamUrl = baseUrl + "/Videos/" + request.ItemId + "/stream?api_key=" + server.ApiKey;

			_logger.Info("Proxying stream from remote server {0} for item {1}", server.Name, request.ItemId);

			var stream = await _httpClient.Get(new HttpRequestOptions
			{
				Url = streamUrl,
				CancellationToken = CancellationToken.None,
				BufferContent = false
			}).ConfigureAwait(false);

			return ResultFactory.GetResult(Request, stream, "video/mp4");
		}
	}
}
