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
