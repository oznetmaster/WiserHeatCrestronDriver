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
public sealed partial class PlatformDiscoveryTests
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
	[Test]
	public async Task LifetimeIdentitySurvivesRefreshAndIsPublishedByDispatcher ()
		{
		string lifetime = _driver.DriverLifetimeId;
		Assert.That (Guid.TryParseExact (lifetime, "N", out _), Is.True);
		Assert.That (await _driver.RefreshSystemStateAsync (true), Is.True);
		using var dispatcher = CreateDispatcher ();
		Assert.That (_driver.DriverLifetimeId, Is.EqualTo (lifetime));
		Assert.That (dispatcher.GetState (DriverController.RootControllerId).PropertyValues["driverLifetimeId"].GetValue<string> (), Is.EqualTo (lifetime));
		}

	[Test]
	public void NewRootEntityHasDifferentLifetimeIdentity ()
		{
		using var replacement = new WiserPlatformDriver (new DriverControllerCreationArgs ("wiser-platform-test", TestSupport.DataDirectory, _logger.AppLogger, null), TestSupport.Resources (_logger));
		Assert.That (replacement.DriverLifetimeId, Is.Not.EqualTo (_driver.DriverLifetimeId));
		}

	[Test]
	public async Task SuccessfulFreshReadPublishesTimestampButCachedReadDoesNotRenewIt ()
		{
		Assert.That (_driver.LastHubRefreshUtc, Is.Empty);
		var started = DateTimeOffset.UtcNow;
		Assert.That (await _driver.RefreshSystemStateAsync (true), Is.True);
		string stamp = _driver.LastHubRefreshUtc;
		Assert.That (stamp, Does.StartWith ("utc:"));
		var observed = DateTimeOffset.ParseExact (stamp.Substring (4), "O", System.Globalization.CultureInfo.InvariantCulture);
		Assert.That (observed, Is.GreaterThanOrEqualTo (started).And.LessThanOrEqualTo (DateTimeOffset.UtcNow));
		Assert.That (observed.Offset, Is.EqualTo (TimeSpan.Zero));
		Assert.That (await _driver.RefreshSystemStateAsync (), Is.True);
		Assert.That (_driver.LastHubRefreshUtc, Is.EqualTo (stamp));
		}

	[Test]
	public async Task FailedFreshReadDoesNotRenewSuccessfulTimestamp ()
		{
		Assert.That (await _driver.RefreshSystemStateAsync (true), Is.True);
		string stamp = _driver.LastHubRefreshUtc;
		_transport.FailScheduleRead = true;
		Assert.That (await _driver.RefreshSystemStateAsync (true), Is.False);
		Assert.That (_driver.LastHubRefreshUtc, Is.EqualTo (stamp));
		}

	[Test]
	public async Task ReplacedConnectionGenerationCannotPublishFreshTimestamp ()
		{
		Assert.That (await _driver.RefreshSystemStateAsync (true), Is.True);
		string stamp = _driver.LastHubRefreshUtc;
		_transport.HoldNextDomain = true;
		var pending = _driver.RefreshSystemStateAsync (true);
		await TestSupport.Complete (_transport.Entered.Task);
		Set ("_connectionGeneration", Field<long> ("_connectionGeneration") + 1);
		_transport.Release.TrySetResult (true);
		await TestSupport.Complete (pending);
		Assert.That (await pending, Is.False);
		Assert.That (_driver.LastHubRefreshUtc, Is.EqualTo (stamp));
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
	public async Task RoomStateRead_DoesNotWithdrawExistingController ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"First room\"}]");
		using var dispatcher = CreateDispatcher ();
		var withdrawn = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		var readded = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		dispatcher.ControllerIdsChanged += (_, _) =>
			{
			if (!dispatcher.ControllerIds.Contains ("room_4"))
				withdrawn.TrySetResult (true);
			else if (withdrawn.Task.IsCompleted)
				readded.TrySetResult (true);
			};
		Assert.That (dispatcher.GetState ("room_4").PropertyValues.Keys, Does.Contain ("extension:uiDefinition"));
		await Task.WhenAny (withdrawn.Task, Task.Delay (500));
		if (withdrawn.Task.IsCompleted)
			await TestSupport.Complete (readded.Task);
		Assert.That (withdrawn.Task.IsCompleted, Is.False,
			"A read must not tell Home that an installed room controller has disappeared.");
		}
	private DriverController CreateDispatcher () => EntryPoint.CreateController (_driver,
		new DriverControllerCreationArgs ("wiser-platform-test", TestSupport.DataDirectory, _logger.AppLogger, null));
	[Test]
	public async Task Rooms_AdvertiseDocumentedHvacCategory ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\"}]");
		Assert.Multiple (() =>
			{
			Assert.That (_driver.ManagedDevices["room_4"].UxCategory, Is.EqualTo (DeviceUxCategory.Hvac),
				"Crestron's DeviceUxCategory contract categorizes thermostats as Hvac.");
			Assert.That (Entities["room_4"].UxCategory, Is.EqualTo (DeviceUxCategory.Hvac));
			});
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task FirstRoomSnapshot_IsReadyWhetherDiscoveredBeforeOrAfterDispatcher (bool discoveredBeforeDispatcher)
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Test room\",\"CurrentSetPoint\":205}]";
		if (discoveredBeforeDispatcher)
			await Refresh (rooms);
		using var dispatcher = CreateDispatcher ();
		if (!discoveredBeforeDispatcher)
			await Refresh (rooms);
		var first = dispatcher.GetState ("room_4");
		var second = dispatcher.GetState ("room_4");
		Assert.Multiple (() =>
			{
			foreach (var state in new[] { first, second })
				{
				Assert.That (state.PropertyValues["readyIndicator:isReady"].GetValue<bool> (), Is.True);
				Assert.That (state.PropertyValues["onlineIndicator:isOnline"].GetValue<bool> (), Is.True);
				Assert.That (state.PropertyValues["targetTemperature"].GetValue<double> (), Is.EqualTo (20.5));
				}
			Assert.That (dispatcher.GetStatus ("room_4"), Is.EqualTo (DriverControllerStatus.Running));
			});
		}

	[Test]
	public async Task StoppedAndRecoveredRoomSnapshots_PreservePriorObservedAvailability ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\"}]");
		using var dispatcher = CreateDispatcher ();
		Entities["room_4"].StopPolling ();
		var stopped = dispatcher.GetState ("room_4");
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\"}]");
		var recovered = dispatcher.GetState ("room_4");
		Assert.Multiple (() =>
			{
			foreach (string property in new[] { "readyIndicator:isReady", "onlineIndicator:isOnline" })
				{
				Assert.That (stopped.PropertyValues[property].GetValue<bool> (), Is.False);
				Assert.That (recovered.PropertyValues[property].GetValue<bool> (), Is.True);
				}
			});
		}

	[Test]
	public async Task LateDiscoveredRoom_AnnouncesItsExtensionWithoutWithdrawingExistingRooms ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"First room\"}]");
		var originalRoom = Entities["room_4"];
		using var dispatcher = CreateDispatcher ();
		var announced = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		int uiAnnouncements = 0;
		var registrations = new System.Collections.Concurrent.ConcurrentQueue<bool> ();
		dispatcher.ControllerIdsChanged += (_, _) => registrations.Enqueue (dispatcher.ControllerIds.Contains ("room_4"));
		dispatcher.ValuesChanged += (_, args) =>
			{
			if (args.ControllerId == "room_5" && args.Update.Changes.ContainsKey ("extension:uiDefinition"))
				{
				Interlocked.Increment (ref uiAnnouncements);
				announced.TrySetResult (true);
				}
			};
		await Refresh ("[{\"id\":4,\"Name\":\"First room\"},{\"id\":5,\"Name\":\"Second room\"}]");
		await TestSupport.Complete (announced.Task);
		Entities["room_5"].StartPolling ();
		Assert.Multiple (() =>
			{
				Assert.That (Entities["room_4"], Is.SameAs (originalRoom));
				Assert.That (registrations, Is.Not.Empty.And.All.True);
				Assert.That (dispatcher.ControllerIds, Does.Contain ("room_4").And.Contain ("room_5"));
				Assert.That (dispatcher.GetState ("room_5").PropertyValues.Keys, Does.Contain ("extension:uiDefinition"));
				Assert.That (_driver.ManagedDevices.Keys, Is.EquivalentTo (new[] { "room_4", "room_5" }));
				Assert.That (uiAnnouncements, Is.EqualTo (1), "Ordinary repeated polling must not repeat the registration announcement.");
			});
		}
	[Test]
	public async Task RoomDispatcher_TranslationsAndRemovalPreserveOtherRooms ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"First room\"},{\"id\":5,\"Name\":\"Second room\"}]");
		using var dispatcher = CreateDispatcher ();
		Assert.That (dispatcher.SupportedCultures, Does.Contain ("en-US"));
		Assert.That (dispatcher.GetLanguageTranslations ("en-US"), Is.Not.Empty);
		await Refresh ("[{\"id\":5,\"Name\":\"Second room\"}]");
		Assert.That (dispatcher.GetState ("room_4").PropertyValues, Is.Empty,
			"A genuinely removed room must not be served from stale state.");
		Assert.That (dispatcher.ControllerIds, Does.Not.Contain ("room_4").And.Contain ("room_5"));
		Assert.That (dispatcher.GetState ("room_5").PropertyValues.Keys, Does.Contain ("extension:uiDefinition"));
		Assert.That (_driver.ManagedDevices.Keys, Is.EquivalentTo (new[] { "room_5" }));
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
	[TestCase ("day")]
	[TestCase ("time")]
	[TestCase ("temperature")]
	public async Task ScheduleEditor_SelectionPublishesChangedValuesWithoutHubPoll (string selection)
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Test schedule\",\"Monday\":{\"Time\":[600,2200],\"DegreesC\":[210,170]},\"Tuesday\":{\"Time\":[900],\"DegreesC\":[190]}}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		using var dispatcher = CreateDispatcher ();
		room.StartPolling ();
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		var observed = new System.Collections.Concurrent.ConcurrentDictionary<string, DriverEntityValue> ();
		var published = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		dispatcher.ValuesChanged += (_, args) =>
			{
			if (args.ControllerId != "room_4")
				return;
			foreach (var change in args.Update.Changes)
				if (change.Value.Value.HasValue)
					observed[change.Key] = change.Value.Value.Value;
			bool Match<T> (string key, T expected) => observed.TryGetValue (key, out var value) && Equals (value.GetValue<T> (), expected);
			bool complete = selection == "time" ? Match ("editSlot1Time", "07:00") : selection == "temperature" ? Match ("editSlot1Temperature", 22d) :
				Match ("editSelectedDay", "Tuesday") && Match ("editSlot1Time", "09:00") && Match ("editSlot1Temperature", 19d) && Match ("editSlot2Visible", false);
			if (complete)
				published.TrySetResult (true);
			};
		if (selection == "day")
			room.SetEditSelectedDay ("Tuesday");
		else if (selection == "time")
			room.SetEditSlot1Time ("07:00");
		else
			room.SetEditSlot1Temperature (22);
		await TestSupport.Complete (published.Task);
		Assert.That (_transport.ScheduleWrites, Is.Zero, "Editor selections must publish without saving the schedule.");
		}

	[Test]
	public async Task ScheduleEditor_UnchangedHubPollPreservesUnsavedEditsUntilCancel ()
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Test schedule\",\"Monday\":{\"Time\":[600,2200],\"DegreesC\":[210,170]}}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.SetEditSlot1Time ("07:00");
		room.SetEditSlot1Temperature (22);
		await Refresh (rooms);
		Assert.That (room.EditSlot1Time, Is.EqualTo ("07:00"), "Polling must not discard an in-progress edit.");
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (22));
		room.CancelEditSchedule ();
		Assert.That (room.EditSlot1Time, Is.EqualTo ("06:00"));
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (21));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task ScheduleEditor_ChangedScheduleCannotBeSavedFromStaleBuffer (bool pollBeforeSave)
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Test schedule\",\"Monday\":{\"Time\":[600],\"DegreesC\":[210]}}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.SetEditSlot1Time ("07:00");
		_transport.HeatingSchedules = _transport.HeatingSchedules.Replace ("[600]", "[800]");
		if (pollBeforeSave)
			await Refresh (rooms);
		room.SaveEditScheduleDay ();
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That (_transport.ScheduleWrites, Is.Zero, "A stale editor must not overwrite the hub.");
		Assert.That (room.EditScheduleError, Does.Contain ("Schedule changed"));
		Assert.That (room.EditSlot1Time, Is.EqualTo ("07:00"), "Keep the pending edit visible with the conflict message until Cancel.");
		room.CancelEditSchedule ();
		Assert.That (room.EditSlot1Time, Is.EqualTo ("08:00"));
		Assert.That (room.EditScheduleError, Is.Empty);
		}

	[Test]
	public async Task ScheduleEditor_ReassignedRoomCannotWritePreviousSchedule ()
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"First\",\"Monday\":{\"Time\":[600],\"DegreesC\":[210]}},{\"id\":9,\"Name\":\"Second\",\"Monday\":{\"Time\":[900],\"DegreesC\":[190]}}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.SetEditSlot1Time ("07:00");
		_transport.Rooms = rooms.Replace ("\"ScheduleId\":7", "\"ScheduleId\":9");
		room.SaveEditScheduleAllDays ();
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That (_transport.ScheduleWrites, Is.Zero);
		Assert.That (room.EditScheduleError, Does.Contain ("Schedule changed"));
		room.CancelEditSchedule ();
		Assert.That (room.EditSlot1Time, Is.EqualTo ("09:00"));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task ScheduleEditor_SaveWritesOnlyTheRequestedDaysAndReloadsObservedData (bool allDays)
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7}]";
		string days = string.Join (",", Enum.GetNames (typeof (DayOfWeek)).Select (day => "\"" + day + "\":{\"Time\":[600],\"DegreesC\":[210]}"));
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Test schedule\"," + days + "}]";
		_transport.AllowScheduleWrites = true;
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.SetEditSlot1Time ("07:00");
		room.SetEditSlot1Temperature (22);
		await Refresh (rooms);
		if (allDays)
			room.SaveEditScheduleAllDays ();
		else
			room.SaveEditScheduleDay ();
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That (_transport.ScheduleWrites, Is.EqualTo (1));
		Assert.That (room.EditScheduleError, Is.Empty);
		foreach (string day in Enum.GetNames (typeof (DayOfWeek)))
			{
			string expected = allDays || day == "Monday" ? "{\"Time\":[700],\"DegreesC\":[220]}" : "{\"Time\":[600],\"DegreesC\":[210]}";
			Assert.That (_transport.LastScheduleWrite, Does.Contain ("\"" + day + "\":" + expected));
			}
		room.CancelEditSchedule ();
		Assert.That (room.EditSlot1Time, Is.EqualTo ("07:00"));
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (22));
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
	[TestCase ("read-before")]
	[TestCase ("read-after")]
	[TestCase ("ignored-write")]
	public async Task ScheduleEditor_UnconfirmedSaveKeepsPendingEditsAndReportsFailure (string failure)
		{
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Test schedule\",\"Monday\":{\"Time\":[600],\"DegreesC\":[210]}}]";
		_transport.AllowScheduleWrites = true;
		await _api.ReadHubDataAsync ();
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7}]");
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.SetEditSlot1Time ("07:00");
		_transport.FailScheduleRead = failure == "read-before";
		_transport.FailScheduleReadAfterWrite = failure == "read-after";
		_transport.IgnoreScheduleWrite = failure == "ignored-write";
		room.SaveEditScheduleDay ();
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That (room.EditScheduleError, Is.Not.Empty, "An HTTP acknowledgement alone does not establish a saved schedule.");
		Assert.That (room.EditSlot1Time, Is.EqualTo ("07:00"), "Keep the pending edit visible when its save cannot be confirmed.");
		Assert.That (_transport.ScheduleWrites, Is.EqualTo (failure == "read-before" ? 0 : 1), "Never automatically repeat an uncertain schedule write.");
		}
	[TestCase ("boost", false)]
	[TestCase ("boost", true)]
	[TestCase ("cancel boost", false)]
	[TestCase ("cancel boost", true)]
	[TestCase ("advance schedule", false)]
	[TestCase ("advance schedule", true)]
	[TestCase ("disable schedule", false)]
	[TestCase ("disable schedule", true)]
	[TestCase ("enable schedule", false)]
	[TestCase ("enable schedule", true)]
	[TestCase ("adjust setpoint", false)]
	[TestCase ("adjust setpoint", true)]
	[TestCase ("set setpoint", false)]
	[TestCase ("set setpoint", true)]
	public async Task RoomCommands_RefreshObservedStateInsidePollingInterval (string command, bool refreshFails)
		{
		_transport.AllowRoomCommands = true;
		string origin = command == "cancel boost" ? "FromBoost" : "FromSchedule";
		await Refresh ("[{\"id\":4,\"Name\":\"Before command\",\"ScheduleId\":7,\"Mode\":\"Auto\",\"CurrentSetPoint\":205,\"SetPointOrigin\":\"" + origin + "\"}]");
		Set ("_lastScheduleRefreshUtc", DateTimeOffset.UtcNow);
		int readsBeforeCommand = _transport.DomainReads;
		_transport.FailScheduleRead = refreshFails;
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
			case "enable schedule":
				accepted = await _driver.SetRoomScheduleEnabledAsync (4, true);
				break;
			case "adjust setpoint":
				accepted = await _driver.AdjustRoomSetpointAsync (4, 0.5);
				break;
			case "set setpoint":
				accepted = await _driver.SetRoomSetpointAsync (4, 21);
				break;
			default:
				accepted = await _driver.SetRoomScheduleEnabledAsync (4, false);
				break;
			}
		Assert.That (accepted, Is.EqualTo (!refreshFails), "Completion must not report success when its fresh hub read failed.");
		Assert.That (_transport.RoomCommands, Is.GreaterThan (0));
		Assert.That (_transport.DomainReads, Is.GreaterThan (readsBeforeCommand));
		Assert.That (_driver.ManagedDevices["room_4"].Name, Is.EqualTo (refreshFails ? "Before command" : "Hub confirmed"), "Only a successful hub read may publish the new state.");
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
	[Test]
	public async Task RestartingExistingRoom_RestoresReadyAndOnlineIndicators ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\"}]");
		var room = Entities["room_4"];
		room.StopPolling ();
		Assert.That (room.ReadyIndicatorIsReady, Is.False);
		Assert.That (room.OnlineIndicatorIsOnline, Is.False);
		room.StartPolling ();
		Assert.That (room.ReadyIndicatorIsReady, Is.True);
		Assert.That (room.OnlineIndicatorIsOnline, Is.True);
		}

	[Test]
	public async Task SuccessfulReconnect_RestoresExistingRoomReadinessWithoutReplacingController ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\",\"CurrentSetPoint\":205}]");
		var room = Entities["room_4"];
		using var dispatcher = CreateDispatcher ();
		int registrationChanges = 0;
		dispatcher.ControllerIdsChanged += (_, _) => Interlocked.Increment (ref registrationChanges);
		_driver.ApiFactory = (_, _, _) => throw new InvalidOperationException ("Injected connection failure.");
		await TestSupport.Complete (Connect ());
		Assert.That (_driver.ReadyIndicatorIsReady, Is.False);
		Assert.That (room.ReadyIndicatorIsReady, Is.False);
		Assert.That (room.OnlineIndicatorIsOnline, Is.False);
		using var recovery = new SnapshotTransport { Rooms = "[{\"id\":4,\"Name\":\"Test room\",\"CurrentSetPoint\":215}]" };
		_driver.ApiFactory = (_, _, _) => CreateApi (recovery);
		await TestSupport.Complete (Connect ());
		Assert.Multiple (() =>
			{
			Assert.That (_driver.ReadyIndicatorIsReady, Is.True);
			Assert.That (Entities["room_4"], Is.SameAs (room));
			Assert.That (room.ReadyIndicatorIsReady, Is.True, "A recovered gateway must not leave its existing room permanently not ready.");
			Assert.That (room.OnlineIndicatorIsOnline, Is.True);
			Assert.That (room.TargetTemperature, Is.EqualTo (21.5));
			Assert.That (registrationChanges, Is.Zero, "Recover state without withdrawing an installed controller.");
			});
		}

	[Test]
	public async Task FailedRefresh_DoesNotRestoreStoppedRoomAvailability ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\"}]");
		var room = Entities["room_4"];
		room.StopPolling ();
		_transport.FailScheduleRead = true;
		Assert.That (await _driver.RefreshSystemStateAsync (true), Is.False);
		Assert.That (room.ReadyIndicatorIsReady, Is.False);
		Assert.That (room.OnlineIndicatorIsOnline, Is.False);
		}
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
		internal readonly System.Collections.Concurrent.ConcurrentQueue<(LogEntryLevel Level, string Message)> Entries = new ();
		public override bool IsEnabled (string id, LogEntryLevel level) => true;
		public override LogEntryLevel GetCurrentLevel (string id) => LogEntryLevel.Info;
		public override void Exception (string id, Exception exception, string message, params object[] args)
			{
			Log (id, LogEntryLevel.Error, message + " " + exception, args);
			}
		public override void Log (string id, LogEntryLevel level, string message)
			{
			Entries.Enqueue ((level, message));
			}
		public override void Log (string id, LogEntryLevel level, string message, params object[] args)
			{
			Log (id, level, string.Format (System.Globalization.CultureInfo.InvariantCulture, message, args));
			}
		public override void Log<T1> (string id, LogEntryLevel level, string message, T1 arg1)
			{
			Log (id, level, message, new object[] { arg1 });
			}
		public override void Log<T1, T2> (string id, LogEntryLevel level, string message, T1 arg1, T2 arg2)
			{
			Log (id, level, message, new object[] { arg1, arg2 });
			}
		public override void Log<T1, T2, T3> (string id, LogEntryLevel level, string message, T1 arg1, T2 arg2, T3 arg3)
			{
			Log (id, level, message, new object[] { arg1, arg2, arg3 });
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
		Assert.That (room.ControlStatus, Does.Contain ("\"Completed\":1,\"Pending\":0"), "The command did not complete; do not inspect intermediate state as its result.");
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
		internal int ScheduleWrites;
		internal int ScheduleAssignments;
		internal bool? AcceptScheduleAssignment;
		internal bool IgnoreScheduleAssignment;
		internal bool AllowScheduleWrites;
		internal bool IgnoreScheduleWrite;
		internal bool FailScheduleRead;
		internal bool FailScheduleReadAfterWrite;
		internal string LastScheduleWrite;
		internal bool HoldNextDomain;
		internal readonly TaskCompletionSource<bool> Entered = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal readonly TaskCompletionSource<bool> Release = new (TaskCreationOptions.RunContinuationsAsynchronously);
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			if (request.Method.Method == "PATCH" && request.RequestUri.AbsolutePath.EndsWith ("/schedules/Assign"))
				{
				ScheduleAssignments++;
				Assert.That (AcceptScheduleAssignment.HasValue, Is.True, "This scenario must not assign a schedule.");
				if (AcceptScheduleAssignment == false)
					return new HttpResponseMessage (HttpStatusCode.BadRequest) { Content = new StringContent ("{}") };
				string assignment = await request.Content.ReadAsStringAsync ();
				Assert.That (assignment, Does.Contain ("\"id\":9"));
				if (!IgnoreScheduleAssignment)
					Rooms = Rooms.Replace ("\"ScheduleId\":7", "\"ScheduleId\":9");
				return new HttpResponseMessage (HttpStatusCode.NoContent);
				}
			if (request.Method.Method == "PATCH" && request.RequestUri.AbsolutePath.EndsWith ("/schedules/Heating/7"))
				{
				ScheduleWrites++;
				Assert.That (AllowScheduleWrites, Is.True, "This scenario must not write a schedule.");
				LastScheduleWrite = await request.Content.ReadAsStringAsync ();
				if (!IgnoreScheduleWrite)
					HeatingSchedules = "[{\"id\":7,\"Name\":\"Test schedule\"," + LastScheduleWrite.Substring (1) + "]";
				return new HttpResponseMessage (HttpStatusCode.NoContent);
				}
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
			if (path.EndsWith ("/schedules/") && (FailScheduleRead || (FailScheduleReadAfterWrite && ScheduleWrites != 0)))
				return new HttpResponseMessage (HttpStatusCode.BadRequest) { Content = new StringContent ("{}") };
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