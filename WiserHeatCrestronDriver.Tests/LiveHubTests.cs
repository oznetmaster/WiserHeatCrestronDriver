// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

using System.Threading.Tasks;

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Logging;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

using NUnit.Framework;

using WiserHeat.CrestronDriver;

namespace WiserHeatCrestronDriver.Tests;

// These fixtures read the hub through real driver discovery. No room control commands are sent.
[TestFixture, Category ("Processor"), Category ("Live"), NonParallelizable]
public sealed class LiveHubTests
	{
	private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
	private DriverLogger _logger;
	private WiserPlatformDriver _driver;
	private string _secret;

	[SetUp]
	public async Task ConnectDriver ()
		{
		string flag = TestContext.Parameters.Get ("EnableLiveTests", "");
		if (flag.Length != 0 && !bool.TryParse (flag, out _))
			throw new InvalidDataException ("EnableLiveTests must be true or false.");
		if (flag.Equals ("false", StringComparison.OrdinalIgnoreCase))
			Assert.Ignore ("Live tests are disabled for this run.");
		string directory = TestContext.Parameters.Get ("TestDataDirectory", "");
		string path = Path.Combine (string.IsNullOrWhiteSpace (directory)
			? Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "WiserHeatAPIv2") : directory, "LiveTestSettings.json");
		LiveSettings settings = null;
		if (File.Exists (path))
			{
			try
				{
				using var input = File.OpenRead (path);
				settings = (LiveSettings)new DataContractJsonSerializer (typeof (LiveSettings)).ReadObject (input);
				}
			catch (SerializationException) { throw new InvalidDataException ("LiveTestSettings.json must contain a valid settings object."); }
			}
		if (!flag.Equals ("true", StringComparison.OrdinalIgnoreCase) && settings?.Enabled != true)
			Assert.Ignore ("Live driver tests require private settings and explicit enablement.");
		if (string.IsNullOrWhiteSpace (settings?.HubHost) || string.IsNullOrWhiteSpace (settings.Secret))
			throw new InvalidDataException ("Enabled live tests require hubHost and secret in LiveTestSettings.json.");
#if NETFRAMEWORK
		if (Type.GetType ("Mono.Runtime") == null)
			Assert.Ignore ("Run live driver fixtures through the desktop SDK harness or processor runtime.");
#endif
		_logger = new DriverLogger ("wiser-live-test") { AppLogger = new LiveLogger (settings.Secret) };
		_secret = settings.Secret;
		_driver = new WiserPlatformDriver (new DriverControllerCreationArgs ("wiser-live-test", TestSupport.DataDirectory, _logger.AppLogger, null), TestSupport.Resources (_logger));
		typeof (WiserPlatformDriver).GetField ("_hubIpAddress", PRIVATE).SetValue (_driver, settings.HubHost);
		typeof (WiserPlatformDriver).GetField ("_hubSecret", PRIVATE).SetValue (_driver, settings.Secret);
		await Connect ();
		}

	[TearDown]
	public void DisposeDriver ()
		{
		_driver?.Dispose ();
		_logger?.Dispose ();
		}

	private async Task Connect ()
		{
		await (Task)typeof (WiserPlatformDriver).GetMethod ("ConnectAndDiscoverAsync", PRIVATE).Invoke (_driver, null);
		// Keep subsequent observations under the fixture's control instead of its ten-second background loop.
		typeof (WiserPlatformDriver).GetMethod ("StopRefreshLoop", PRIVATE).Invoke (_driver, null);
		Assert.That (_driver.OnlineIndicatorIsOnline && _driver.ReadyIndicatorIsReady, Is.True,
			"Driver discovery must authenticate and become ready. " + _driver.PlatformLastError.Replace (_secret, "[redacted]"));
		}

	private Dictionary<string, WiserRoomEntity> Rooms => (Dictionary<string, WiserRoomEntity>)typeof (WiserPlatformDriver).GetField ("_roomEntities", PRIVATE).GetValue (_driver);

	private sealed class LiveLogger (string secret) : DriverControllerLogger
		{
		public override bool IsEnabled (string id, LogEntryLevel level) => level == LogEntryLevel.Error;
		public override LogEntryLevel GetCurrentLevel (string id) => LogEntryLevel.Error;
		public override void Exception (string id, Exception exception, string message, params object[] args) => Log (id, LogEntryLevel.Error, message + " " + exception, args);
		public override void Log (string id, LogEntryLevel level, string message)
			{
			if (IsEnabled (id, level))
				TestContext.Progress.WriteLine (message.Replace (secret, "[redacted]"));
			}
		public override void Log (string id, LogEntryLevel level, string message, params object[] args) => Log (id, level, string.Format (CultureInfo.InvariantCulture, message, args));
		public override void Log<T> (string id, LogEntryLevel level, string message, T arg) => Log (id, level, message, new object[] { arg });
		public override void Log<T1, T2> (string id, LogEntryLevel level, string message, T1 arg1, T2 arg2) => Log (id, level, message, new object[] { arg1, arg2 });
		public override void Log<T1, T2, T3> (string id, LogEntryLevel level, string message, T1 arg1, T2 arg2, T3 arg3) => Log (id, level, message, new object[] { arg1, arg2, arg3 });
		}

	[DataContract]
	private sealed class LiveSettings
		{
		[DataMember (Name = "enabled")]
		public bool Enabled
			{
			get; set;
			}
		[DataMember (Name = "hubHost")]
		public string HubHost
			{
			get; set;
			}
		[DataMember (Name = "secret")]
		public string Secret
			{
			get; set;
			}
		}

	[Test]
	public void RealHub_DiscoveryPublishesReadyRoomEntities ()
		{
		Assert.That (Rooms, Is.Not.Empty, "The selected hub must contain at least one configured room.");
		Assert.That (_driver.ManagedDevices.Keys, Is.EquivalentTo (Rooms.Keys));
		foreach (WiserRoomEntity room in Rooms.Values)
			Assert.That (room.OnlineIndicatorIsOnline && room.ReadyIndicatorIsReady, Is.True);
		}

	[Test]
	public async Task RealHub_RefreshRetainsRoomIdentityAndReportsObservedTemperatures ()
		{
		var original = Rooms.ToDictionary (pair => pair.Key, pair => pair.Value);
		Assert.That (original, Is.Not.Empty);
		await _driver.RefreshSystemStateAsync (true);
		Assert.That (Rooms.Keys, Is.EquivalentTo (original.Keys));
		foreach (var pair in original)
			{
			Assert.That (Rooms[pair.Key], Is.SameAs (pair.Value));
			Assert.That (double.IsNaN (Rooms[pair.Key].TargetTemperature) || double.IsInfinity (Rooms[pair.Key].TargetTemperature), Is.False);
			}
		}

	[Test]
	public async Task RealHub_ReconnectPreservesPublishedRoomIds ()
		{
		string[] original = Rooms.Keys.ToArray ();
		Assert.That (original, Is.Not.Empty);
		await Connect ();
		Assert.That (Rooms.Keys, Is.EquivalentTo (original));
		Assert.That (_driver.ManagedDevices.Keys, Is.EquivalentTo (original));
		}
	}