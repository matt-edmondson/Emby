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
