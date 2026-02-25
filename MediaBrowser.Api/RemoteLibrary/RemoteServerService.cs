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
	/// <summary>
	/// Streaming mode for remote media content.
	/// </summary>
	public enum RemoteStreamingMode
	{
		Direct,
		Proxy,
		Auto
	}

	/// <summary>
	/// API DTO representing a remote server connection.
	/// Properties mirror the configuration model in Emby.Server.Implementations.
	/// </summary>
	public class RemoteServerInfoDto
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Url { get; set; }
		public string ApiKey { get; set; }
		public int SyncIntervalMinutes { get; set; }
		public bool IsEnabled { get; set; }
		public string[] LibraryIds { get; set; }
		public RemoteStreamingMode StreamingMode { get; set; }

		public RemoteServerInfoDto()
		{
			Id = Guid.NewGuid().ToString("N");
			SyncIntervalMinutes = 30;
			IsEnabled = true;
			LibraryIds = Array.Empty<string>();
			StreamingMode = RemoteStreamingMode.Direct;
		}
	}

	/// <summary>
	/// API DTO representing the remote library configuration options.
	/// Properties mirror the configuration model in Emby.Server.Implementations.
	/// </summary>
	public class RemoteLibraryOptionsDto
	{
		public RemoteServerInfoDto[] Servers { get; set; }

		public RemoteLibraryOptionsDto()
		{
			Servers = Array.Empty<RemoteServerInfoDto>();
		}
	}

	/// <summary>
	/// Result of a remote server connectivity test.
	/// </summary>
	public class RemoteServerTestResult
	{
		public bool IsSuccess { get; set; }
		public string Message { get; set; }
	}

	[Route("/RemoteServers", "GET", Summary = "Gets all configured remote servers")]
	[Authenticated(Roles = "Admin")]
	public class GetRemoteServers : IReturn<RemoteServerInfoDto[]>
	{
	}

	[Route("/RemoteServers", "POST", Summary = "Adds or updates a remote server connection")]
	[Authenticated(Roles = "Admin")]
	public class UpdateRemoteServer : RemoteServerInfoDto, IReturnVoid
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

	public class RemoteServerService : BaseApiService
	{
		private const string ConfigKey = "remoteservers";

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

		/// <summary>
		/// Reads the remote library configuration and converts it to API DTOs
		/// via JSON round-trip, since the configuration types live in
		/// Emby.Server.Implementations which cannot be referenced from MediaBrowser.Api.
		/// </summary>
		private RemoteLibraryOptionsDto GetOptions()
		{
			var configObject = _config.GetConfiguration(ConfigKey);
			var json = _json.SerializeToString(configObject);
			return _json.DeserializeFromString<RemoteLibraryOptionsDto>(json);
		}

		/// <summary>
		/// Saves the remote library configuration by converting API DTOs back to
		/// the registered configuration type via JSON round-trip.
		/// </summary>
		private void SaveOptions(RemoteLibraryOptionsDto options)
		{
			var configurationType = _config.GetConfigurationType(ConfigKey);
			var json = _json.SerializeToString(options);
			var configObject = _json.DeserializeFromString(json, configurationType);
			_config.SaveConfiguration(ConfigKey, configObject);
		}

		public object Get(GetRemoteServers request)
		{
			var options = GetOptions();
			return ToOptimizedResult(options.Servers);
		}

		public void Post(UpdateRemoteServer request)
		{
			var options = GetOptions();
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
				servers.Add(new RemoteServerInfoDto
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
			SaveOptions(options);
		}

		public void Delete(DeleteRemoteServer request)
		{
			var options = GetOptions();
			options.Servers = options.Servers
				.Where(s => !string.Equals(s.Id, request.Id, StringComparison.OrdinalIgnoreCase))
				.ToArray();
			SaveOptions(options);
		}

		public async Task<object> Post(TestRemoteServer request)
		{
			var options = GetOptions();
			var server = options.Servers.FirstOrDefault(s => string.Equals(s.Id, request.Id, StringComparison.OrdinalIgnoreCase));

			if (server == null)
			{
				return ToOptimizedResult(new RemoteServerTestResult
				{
					IsSuccess = false,
					Message = "Remote server not found."
				});
			}

			try
			{
				var baseUrl = server.Url.TrimEnd('/');
				var testUrl = baseUrl + "/System/Info/Public?api_key=" + server.ApiKey;

				using (var response = await _httpClient.SendAsync(new HttpRequestOptions
				{
					Url = testUrl,
					CancellationToken = CancellationToken.None,
					BufferContent = false
				}, "GET").ConfigureAwait(false))
				{
					return ToOptimizedResult(new RemoteServerTestResult
					{
						IsSuccess = true,
						Message = "Connection successful."
					});
				}
			}
			catch (Exception ex)
			{
				_logger.ErrorException("Failed to connect to remote server {0}", ex, server.Url);
				return ToOptimizedResult(new RemoteServerTestResult
				{
					IsSuccess = false,
					Message = "Could not connect to remote server."
				});
			}
		}
	}
}
