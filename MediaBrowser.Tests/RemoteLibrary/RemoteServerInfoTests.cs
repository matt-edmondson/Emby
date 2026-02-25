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
