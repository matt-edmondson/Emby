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
