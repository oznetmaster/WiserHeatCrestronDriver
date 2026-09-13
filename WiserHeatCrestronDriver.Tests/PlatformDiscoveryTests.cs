// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Crestron.DeviceDrivers.EntityModel.Logging;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

using NUnit.Framework;

using WiserHeat.CrestronDriver;
using WiserHeatApiV2;

namespace WiserHeatCrestronDriver.Tests;

[TestFixture, Category ("Processor")]
public sealed class PlatformDiscoveryTests
	{
	private DriverLogger _logger;
	private WiserPlatformDriver _driver;
	private WiserAPI _api;
	private SnapshotTransport _transport;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	[SetUp]
	public async Task SetUp ()
		{
#if NETFRAMEWORK
		if (Type.GetType ("Mono.Runtime") == null)
			Assert.Ignore ("Requires the SDK desktop harness or the processor runtime.");
#endif
		_logger = new DriverLogger ("wiser-platform-test") { AppLogger = new TestLogger () };
		_driver = new WiserPlatformDriver (new DriverControllerCreationArgs ("wiser-platform-test", TestSupport.DataDirectory, _logger.AppLogger, null), TestSupport.Resources (_logger));
		_transport = new SnapshotTransport ();
		var constructor = typeof (WiserAPI).GetConstructor (Private, null, new[] { typeof (string), typeof (string), typeof (WiserUnits), typeof (HttpMessageHandler) }, null);
		Assert.That (constructor, Is.Not.Null, "The API transport seam is required; tests must not use a real hub.");
		_api = (WiserAPI)constructor.Invoke (new object[] { "hub.invalid", "synthetic-secret", WiserUnits.Metric, _transport });
		await _api.InitializeAsync ();
		Set ("_api", _api);
		Set ("_lastScheduleRefreshUtc", DateTimeOffset.UtcNow);
		}
	[TearDown]
	public void TearDown ()
		{
		_transport?.Release.TrySetResult (true);
		_driver?.Dispose ();
		_api?.Dispose ();
		_logger?.Dispose ();
		}
	private void Set (string name, object value) => typeof (WiserPlatformDriver).GetField (name, Private).SetValue (_driver, value);
	private T Field<T> (string name) => (T)typeof (WiserPlatformDriver).GetField (name, Private).GetValue (_driver);
	private Dictionary<string, WiserRoomEntity> Entities => Field<Dictionary<string, WiserRoomEntity>> ("_roomEntities");
	private void Clear () => typeof (WiserPlatformDriver).GetMethod ("ApplyConfigurationItems", Private).Invoke (_driver, new object[] { DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues, null, null });
	private async Task Refresh (string rooms)
		{
		_transport.Rooms = rooms;
		await TestSupport.Complete (_driver.RefreshSystemStateAsync (true));
		}
	[Test]
	public async Task DiscoveryPublishesStableRoomIdsAndFallbackNames ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"CurrentSetPoint\":205},{\"id\":7,\"CurrentSetPoint\":190}]");
		Assert.That (_driver.ManagedDevices.Keys, Is.EquivalentTo (new[] { "room_4", "room_7" }));
		Assert.That (_driver.ManagedDevices["room_4"].Name, Is.EqualTo ("Office"));
		Assert.That (_driver.ManagedDevices["room_7"].Name, Is.EqualTo ("Room 7"));
		Assert.That (Entities["room_4"].GetState ().PropertyValues["onlineIndicator:isOnline"].GetValue<bool> (), Is.True);
		}
	[Test]
	public async Task RefreshRetainsChildIdentityAndUpdatesObservedNameAndTemperature ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Study\",\"CurrentSetPoint\":205}]");
		var first = Entities["room_4"];
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"CurrentSetPoint\":215}]");
		Assert.That (Entities["room_4"], Is.SameAs (first));
		Assert.That (_driver.ManagedDevices["room_4"].Name, Is.EqualTo ("Office"));
		Assert.That (first.TargetTemperature, Is.EqualTo (21.5));
		}
	[Test]
	public async Task RemovedRoomGoesOfflineAndRediscoveryCreatesANewChild ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"CurrentSetPoint\":205}]");
		var first = Entities["room_4"];
		await Refresh ("[]");
		Assert.That (_driver.ManagedDevices, Is.Empty);
		Assert.That (first.GetState ().PropertyValues["onlineIndicator:isOnline"].GetValue<bool> (), Is.False);
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"CurrentSetPoint\":220}]");
		Assert.That (Entities["room_4"], Is.Not.SameAs (first));
		Assert.That (Entities["room_4"].TargetTemperature, Is.EqualTo (22d));
		}
	[Test]
	public async Task ClearingConfigurationRemovesChildrenAndConnectionCredentials ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Office\"}]");
		var child = Entities["room_4"];
		Set ("_hubIpAddress", "hub.invalid");
		Set ("_hubSecret", "synthetic-secret");
		Clear ();
		Assert.Multiple (() =>
			{
			Assert.That (_driver.ManagedDevices, Is.Empty);
			Assert.That (Entities, Is.Empty);
			Assert.That (child.GetState ().PropertyValues["onlineIndicator:isOnline"].GetValue<bool> (), Is.False);
			Assert.That (Field<string> ("_hubIpAddress"), Is.Empty);
			Assert.That (Field<string> ("_hubSecret"), Is.Empty);
			});
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task LateRefreshCannotRestoreChildrenAfterClearOrDispose (bool dispose)
		{
		_transport.Rooms = "[{\"id\":4,\"Name\":\"Late room\"}]";
		_transport.HoldNextDomain = true;
		var refresh = _driver.RefreshSystemStateAsync (true);
		await TestSupport.Complete (_transport.Entered.Task);
		if (dispose) _driver.Dispose (); else Clear ();
		_transport.Release.TrySetResult (true);
		await TestSupport.Complete (refresh);
		Assert.That (_driver.ManagedDevices, Is.Empty);
		Assert.That (Entities, Is.Empty);
		}
	private WiserAPI CreateApi (SnapshotTransport transport)
		{
		var constructor = typeof (WiserAPI).GetConstructor (Private, null, new[] { typeof (string), typeof (string), typeof (WiserUnits), typeof (HttpMessageHandler) }, null);
		return (WiserAPI)constructor.Invoke (new object[] { "hub.invalid", "synthetic-secret", WiserUnits.Metric, transport });
		}
	private Task Connect () => (Task)typeof (WiserPlatformDriver).GetMethod ("ConnectAndDiscoverAsync", Private).Invoke (_driver, null);
	[TestCase (false)]
	[TestCase (true)]
	public async Task LateConnectionCannotRepublishAfterClearOrDispose (bool dispose)
		{
		using var transport = new SnapshotTransport { Rooms = "[{\"id\":4,\"Name\":\"Late room\"}]", HoldNextDomain = true };
		_driver.ApiFactory = (host, secret, units) => CreateApi (transport);
		var connect = Connect ();
		try
			{
			await TestSupport.Complete (transport.Entered.Task);
			if (dispose) _driver.Dispose (); else Clear ();
			}
		finally { transport.Release.TrySetResult (true); }
		await TestSupport.Complete (connect);
		Assert.That (Field<WiserAPI> ("_api"), Is.Null);
		Assert.That (_driver.ManagedDevices, Is.Empty);
		Assert.That (Field<Task> ("_refreshLoopTask"), Is.Null);
		}
	[Test]
	public async Task OlderConnectionCannotReplaceNewerConnection ()
		{
		using var older = new SnapshotTransport { Rooms = "[{\"id\":1,\"Name\":\"Old room\"}]", HoldNextDomain = true };
		using var newer = new SnapshotTransport { Rooms = "[{\"id\":2,\"Name\":\"New room\"}]" };
		_driver.ApiFactory = (host, secret, units) => CreateApi (older);
		var oldConnect = Connect ();
		WiserAPI current = null;
		try
			{
			await TestSupport.Complete (older.Entered.Task);
			_driver.ApiFactory = (host, secret, units) => CreateApi (newer);
			await TestSupport.Complete (Connect ());
			current = Field<WiserAPI> ("_api");
			}
		finally { older.Release.TrySetResult (true); }
		await TestSupport.Complete (oldConnect);
		Assert.That (Field<WiserAPI> ("_api"), Is.SameAs (current));
		Assert.That (_driver.ManagedDevices.Keys, Is.EquivalentTo (new[] { "room_2" }));
		}
	private sealed class TestLogger : DriverControllerLogger
		{
		public override bool IsEnabled (string id, LogEntryLevel level) => false;
		public override LogEntryLevel GetCurrentLevel (string id) => LogEntryLevel.Error;
		public override void Exception (string id, Exception exception, string message, params object[] args) { }
		public override void Log (string id, LogEntryLevel level, string message) { }
		public override void Log (string id, LogEntryLevel level, string message, params object[] args) { }
		public override void Log<T1> (string id, LogEntryLevel level, string message, T1 arg1) { }
		public override void Log<T1, T2> (string id, LogEntryLevel level, string message, T1 arg1, T2 arg2) { }
		public override void Log<T1, T2, T3> (string id, LogEntryLevel level, string message, T1 arg1, T2 arg2, T3 arg3) { }
		}
	private sealed class SnapshotTransport : HttpMessageHandler
		{
		internal string Rooms = "[]";
		internal bool HoldNextDomain;
		internal readonly TaskCompletionSource<bool> Entered = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal readonly TaskCompletionSource<bool> Release = new (TaskCreationOptions.RunContinuationsAsynchronously);
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			Assert.That (request.Method, Is.EqualTo (HttpMethod.Get), "Discovery must not operate a physical device.");
			string path = request.RequestUri.AbsolutePath;
			if (path.EndsWith ("/domain/") && HoldNextDomain)
				{
				HoldNextDomain = false;
				Entered.TrySetResult (true);
				await Release.Task; // Deliberately ignore cancellation to exercise a late response.
				}
			string json = path.EndsWith ("/domain/") ? "{\"System\":{},\"Device\":[],\"Room\":" + Rooms + ",\"HeatingChannel\":[],\"Moment\":[]}"
				: path.EndsWith ("/network/") ? "{\"Station\":{}}" : "{\"Heating\":[]}";
			return new HttpResponseMessage (path.EndsWith ("/opentherm/") ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent (json) };
			}
		}
	}