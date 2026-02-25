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
