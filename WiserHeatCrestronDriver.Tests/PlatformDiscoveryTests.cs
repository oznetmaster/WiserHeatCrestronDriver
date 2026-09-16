// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
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
	public async Task SchedulePicker_DefinitionContainsCurrentHubOptions ()
		{
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Office schedule\"}]";
		await _api.ReadHubDataAsync ();
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7}]");
		var state = Entities["room_4"].GetState ();
		var definition = state.Definition.Properties["selectedScheduleId"];
		Assert.That (definition.TypeDef.AvailableValues, Is.Not.Null.And.Not.Empty,
			"The Home extension bridge needs inline options; a reference to a separate array is sent to the app as null.");
		Assert.That (definition.TypeDef.AvailableValues.Select (option => option.Value), Is.EqualTo (new[] { "7" }));
		Assert.That (definition.TypeDef.AvailableValues.Single ().Label.Text, Is.EqualTo ("Office schedule"));
		Assert.That (state.PropertyValues["selectedScheduleId"].GetValue<string> (), Is.EqualTo ("7"));
		}
	[Test]
	public async Task SchedulePicker_RefreshesOptionsWithoutReplacingRoomOrChangingEarlierDefinition ()
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Original\"}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		room.StartPolling ();
		var original = room.GetState ().Definition.Properties["selectedScheduleId"];
		int changes = 0;
		room.DefinitionChanged += (_, _) => changes++;
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Renamed\"},{\"id\":9,\"Name\":\"Other\"}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		Assert.That (Entities["room_4"], Is.SameAs (room));
		var updated = room.GetState ().Definition.Properties["selectedScheduleId"].TypeDef.AvailableValues;
		Assert.That (updated, Is.Not.Null.And.Not.Empty);
		Assert.That (updated.Select (option => option.Value), Is.EquivalentTo (new[] { "7", "9" }));
		Assert.That (updated.Single (option => option.Value == "7").Label.Text, Is.EqualTo ("Renamed"));
		Assert.That (original.TypeDef.AvailableValues.Single ().Label.Text, Is.EqualTo ("Original"));
		Assert.That (changes, Is.EqualTo (1));
		await Refresh (rooms);
		Assert.That (changes, Is.EqualTo (1), "An unchanged poll must not rebuild the UI definition.");
		_transport.HeatingSchedules = "[]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		Assert.That (room.GetState ().Definition.Properties["selectedScheduleId"].TypeDef.AvailableValues, Is.Empty);
		Assert.That (room.ScheduleSelectorEnabled, Is.False);
		Assert.That (changes, Is.EqualTo (2));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task HotWaterCommand_RefreshesObservedStateBeforeReenablingButton (bool initiallyOn)
		{
		_transport.HotWaterState = initiallyOn ? "On" : "Off";
		Set ("_enableWholeHouseHotWater", true);
		Field<WiserWorkQueue> ("_workQueue").SetClient (_api);
		await TestSupport.Complete (_driver.RefreshSystemStateAsync (true));
		Assert.That (_driver.HotWaterIsOn, Is.EqualTo (initiallyOn));
		Set ("_lastScheduleRefreshUtc", DateTimeOffset.UtcNow);
		int readsBeforeCommand = _transport.DomainReads;
		var command = (Task)typeof (WiserPlatformDriver).GetMethod ("ToggleHotWaterAsync", Private).Invoke (_driver, null);
		await TestSupport.Complete (command);
		Assert.That (_transport.HotWaterCommands, Is.EqualTo (1), "One press must send one requested override.");
		Assert.That (_transport.DomainReads, Is.GreaterThan (readsBeforeCommand), "A successful command must bypass the periodic snapshot throttle.");
		Assert.That (_driver.HotWaterIsOn, Is.EqualTo (!initiallyOn), "The UI must reflect the hub's observed state when the command completes.");
		Assert.That (Field<int> ("_hotWaterCommandInProgress"), Is.Zero);
		}
	[TestCase ("boost")]
	[TestCase ("cancel boost")]
	[TestCase ("advance schedule")]
	[TestCase ("disable schedule")]
	public async Task RoomCommands_RefreshObservedStateInsidePollingInterval (string command)
		{
		_transport.AllowRoomCommands = true;
		string origin = command == "cancel boost" ? "FromBoost" : "FromSchedule";
		await Refresh ("[{\"id\":4,\"Name\":\"Before command\",\"ScheduleId\":7,\"Mode\":\"Auto\",\"CurrentSetPoint\":205,\"SetPointOrigin\":\"" + origin + "\"}]");
		Set ("_lastScheduleRefreshUtc", DateTimeOffset.UtcNow);
		int readsBeforeCommand = _transport.DomainReads;
		bool accepted;
		switch (command)
			{
			case "boost":
			case "cancel boost":
				accepted = await _driver.TriggerRoomBoostAsync (4);
				break;
			case "advance schedule":
				accepted = await _driver.AdvanceRoomScheduleAsync (4);
				break;
			default:
				accepted = await _driver.SetRoomScheduleEnabledAsync (4, false);
				break;
			}
		Assert.That (accepted, Is.True);
		Assert.That (_transport.RoomCommands, Is.GreaterThan (0));
		Assert.That (_transport.DomainReads, Is.GreaterThan (readsBeforeCommand));
		Assert.That (_driver.ManagedDevices["room_4"].Name, Is.EqualTo ("Hub confirmed"), "Command completion must publish the returned hub snapshot.");
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task AwayCommand_RefreshesObservedStateInsidePollingInterval (bool initiallyAway)
		{
		_transport.AllowAwayCommands = true;
		_transport.Away = initiallyAway;
		Set ("_allowAwayMode", true);
		Field<WiserWorkQueue> ("_workQueue").SetClient (_api);
		await TestSupport.Complete (_driver.RefreshSystemStateAsync (true));
		Set ("_lastScheduleRefreshUtc", DateTimeOffset.UtcNow);
		int readsBeforeCommand = _transport.DomainReads;
		await TestSupport.Complete ((Task)typeof (WiserPlatformDriver).GetMethod ("ToggleAwayModeAsync", Private).Invoke (_driver, null));
		Assert.That (_transport.AwayCommands, Is.EqualTo (1));
		Assert.That (_transport.DomainReads, Is.GreaterThan (readsBeforeCommand));
		Assert.That (_driver.AwayModeIsEnabled, Is.EqualTo (!initiallyAway));
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
		if (dispose)
			_driver.Dispose ();
		else
			Clear ();
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
			if (dispose)
				_driver.Dispose ();
			else
				Clear ();
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
		public override void Exception (string id, Exception exception, string message, params object[] args)
			{
			}
		public override void Log (string id, LogEntryLevel level, string message)
			{
			}
		public override void Log (string id, LogEntryLevel level, string message, params object[] args)
			{
			}
		public override void Log<T1> (string id, LogEntryLevel level, string message, T1 arg1)
			{
			}
		public override void Log<T1, T2> (string id, LogEntryLevel level, string message, T1 arg1, T2 arg2)
			{
			}
		public override void Log<T1, T2, T3> (string id, LogEntryLevel level, string message, T1 arg1, T2 arg2, T3 arg3)
			{
			}
		}
	[Test]
	public async Task RoomControl_CompletesOnlyAfterFreshHubStateAndRejectsOverlappingScheduleSelection ()
		{
		_transport.AllowRoomCommands = true;
		Set ("_hubIpAddress", "HUB.invalid");
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7,\"Mode\":\"Auto\",\"CurrentSetPoint\":205,\"SetPointOrigin\":\"FromSchedule\"}]");
		var room = Entities["room_4"];
		Assert.That (room.ControlDeviceId, Is.EqualTo ("hub.invalid/room/4"));
		Assert.That (room.GetState ().Definition.Properties.Keys, Does.Contain ("controlStatus"));
		_transport.HoldNextDomain = true;
		room.DisableSchedule ();
		await TestSupport.Complete (_transport.Entered.Task);
		Assert.That (room.ControlStatus, Does.Contain ("\"Completed\":0,\"Pending\":1"));
		int commands = _transport.RoomCommands;
		room.EnableSchedule ();
		room.SetSelectedScheduleId ("7");
		Assert.That (_transport.RoomCommands, Is.EqualTo (commands), "A busy room must reject other commands, including schedule selection.");
		_transport.Release.TrySetResult (true);
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That (room.DeviceLabel, Is.EqualTo ("Office"));
		Assert.That (room.ControlStatus, Does.Contain ("\"Completed\":1,\"Pending\":0"));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task FailedRoomControl_ReleasesActivityAndAcceptsNextCommand (bool throws)
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"CurrentSetPoint\":205}]");
		var room = Entities["room_4"];
		Func<Task<bool>> action = () => throws ? Task.FromException<bool> (new InvalidOperationException ("synthetic failure")) : Task.FromResult (false);
		var method = typeof (WiserRoomEntity).GetMethod ("TryFireAndForgetRoomAction", Private);
		await TestSupport.Complete ((Task)method.Invoke (room, new object[] { action, "test failure" }));
		Assert.That (room.ControlStatus, Does.Contain ("\"Completed\":1,\"Pending\":0"));
		await TestSupport.Complete ((Task)method.Invoke (room, new object[] { (Func<Task<bool>>)(() => Task.FromResult (true)), "next command" }));
		Assert.That (room.ControlStatus, Does.Contain ("\"Completed\":2,\"Pending\":0"));
		}
	private static async Task WaitForCompletion (WiserRoomEntity room)
		{
		for (int i = 0; i < 200 && !room.ControlStatus.Contains ("\"Completed\":1,\"Pending\":0"); i++)
			await Task.Delay (10);
		}
	private sealed class SnapshotTransport : HttpMessageHandler
		{
		internal string Rooms = "[]";
		internal string HeatingSchedules;
		internal string HotWaterState;
		internal bool AllowRoomCommands;
		internal int RoomCommands;
		internal bool AllowAwayCommands;
		internal bool Away;
		internal int AwayCommands;
		internal int HotWaterCommands;
		internal int DomainReads;
		internal bool HoldNextDomain;
		internal readonly TaskCompletionSource<bool> Entered = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal readonly TaskCompletionSource<bool> Release = new (TaskCreationOptions.RunContinuationsAsynchronously);
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			if (request.Method.Method == "PATCH" && request.RequestUri.AbsolutePath.EndsWith ("/domain/HotWater/1") && HotWaterState != null)
				{
				string body = await request.Content.ReadAsStringAsync ();
				bool requestedOn = HotWaterState == "Off";
				Assert.That (body, Does.Contain (requestedOn ? "\"SetPoint\":110" : "\"SetPoint\":-20"));
				HotWaterCommands++;
				HotWaterState = requestedOn ? "On" : "Off";
				return new HttpResponseMessage (HttpStatusCode.NoContent);
				}
			if (request.Method.Method == "PATCH" && request.RequestUri.AbsolutePath.EndsWith ("/domain/Room/4") && AllowRoomCommands)
				{
				RoomCommands++;
				Rooms = Rooms.Replace ("Before command", "Hub confirmed");
				return new HttpResponseMessage (HttpStatusCode.NoContent);
				}
			if (request.Method.Method == "PATCH" && request.RequestUri.AbsolutePath.EndsWith ("/domain/System") && AllowAwayCommands)
				{
				AwayCommands++;
				Away = !Away;
				return new HttpResponseMessage (HttpStatusCode.NoContent);
				}
			Assert.That (request.Method, Is.EqualTo (HttpMethod.Get), "Discovery must not operate a physical device.");
			string path = request.RequestUri.AbsolutePath;
			if (path.EndsWith ("/domain/"))
				DomainReads++;
			if (path.EndsWith ("/domain/") && HoldNextDomain)
				{
				HoldNextDomain = false;
				Entered.TrySetResult (true);
				await Release.Task; // Deliberately ignore cancellation to exercise a late response.
				}
			string json = path.EndsWith ("/domain/") ? "{\"System\":{},\"Device\":[],\"Room\":" + Rooms + ",\"HeatingChannel\":[],\"Moment\":[]}"
				: path.EndsWith ("/network/") ? "{\"Station\":{}}" : "{\"Heating\":[]}";
			if (path.EndsWith ("/schedules/") && AllowRoomCommands)
				json = "{\"Heating\":[{\"id\":7,\"Name\":\"Test schedule\",\"Next\":{\"Day\":\"Monday\",\"Time\":1800,\"DegreesC\":215}}]}";
			if (path.EndsWith ("/schedules/") && HeatingSchedules != null)
				json = "{\"Heating\":" + HeatingSchedules + "}";
			if (path.EndsWith ("/domain/") && AllowAwayCommands)
				json = json.Replace ("\"System\":{}", "\"System\":{\"OverrideType\":\"" + (Away ? "Away" : "None") + "\"}");
			if (path.EndsWith ("/domain/") && HotWaterState != null)
				json = json.Substring (0, json.Length - 1) + ",\"HotWater\":[{\"id\":1,\"HotWaterDescription\":\"FromManualOverride\",\"HotWaterRelayState\":\"" + HotWaterState + "\",\"WaterHeatingState\":\"" + HotWaterState + "\"}]}";
			return new HttpResponseMessage (path.EndsWith ("/opentherm/") ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent (json) };
			}
		}
	}