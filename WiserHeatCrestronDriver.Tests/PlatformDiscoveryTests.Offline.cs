// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Threading.Tasks;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	[Test]
	public async Task UnresponsiveHubRead_IsCancelledAndPublishesOfflineWithoutACommand ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\"}]");
		_driver.HubReadTimeout = TimeSpan.FromMilliseconds (100);
		_transport.StallDomainUntilCancelled = true;
		var read = _driver.RefreshSystemStateAsync (true);
		await TestSupport.Complete (read);
		Assert.That (await read, Is.False);
		Assert.That (_driver.OnlineIndicatorIsOnline, Is.False);
		Assert.That (Entities["room_4"].OnlineIndicatorIsOnline, Is.False);
		_transport.StallDomainUntilCancelled = false;
		Assert.That (await _driver.RefreshSystemStateAsync (), Is.True);
		Assert.That (_driver.OnlineIndicatorIsOnline, Is.True);
		}

	[Test]
	public async Task BackgroundPolling_DetectsOutageAndRecoversWithoutCommandsOrReconfiguration ()
		{
		using var transport = new SnapshotTransport { Rooms = "[{\"id\":4,\"Name\":\"Test room\",\"CurrentSetPoint\":205}]" };
		_driver.ApiFactory = (_, _, _) => CreateApi (transport);
		await TestSupport.Complete (Connect ());
		var room = Entities["room_4"];
		transport.FailScheduleRead = true;
		async Task WaitForOnline (bool expected)
			{
			var until = DateTimeOffset.UtcNow.AddSeconds (20);
			while (_driver.OnlineIndicatorIsOnline != expected && DateTimeOffset.UtcNow < until)
				await Task.Delay (50);
			Assert.That (_driver.OnlineIndicatorIsOnline, Is.EqualTo (expected));
			Assert.That (room.OnlineIndicatorIsOnline, Is.EqualTo (expected));
			}
		await WaitForOnline (false);
		transport.Rooms = "[{\"id\":4,\"Name\":\"Test room\",\"CurrentSetPoint\":215}]";
		transport.FailScheduleRead = false;
		await WaitForOnline (true);
		Assert.That (Entities["room_4"], Is.SameAs (room));
		Assert.That (room.TargetTemperature, Is.EqualTo (21.5));
		Assert.That (transport.RoomCommands + transport.HotWaterCommands + transport.AwayCommands, Is.Zero);
		}

	[Test]
	public async Task FailedHubRead_MarksGatewayAndRoomsOffline_AndFreshReadRecoversSameControllers ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\",\"CurrentSetPoint\":205}]");
		var room = Entities["room_4"];
		typeof (WiserHeat.CrestronDriver.WiserPlatformDriver).GetMethod ("SetOnline", Private).Invoke (_driver, new object[] { true });
		using var dispatcher = CreateDispatcher ();
		int changes = 0;
		dispatcher.ControllerIdsChanged += (_, _) => changes++;
		string stamp = _driver.LastHubRefreshUtc;
		_transport.FailScheduleRead = true;
		Assert.That (await _driver.RefreshSystemStateAsync (true), Is.False);
		Assert.Multiple (() =>
			{
			Assert.That (_driver.OnlineIndicatorIsOnline, Is.False);
			Assert.That (room.OnlineIndicatorIsOnline, Is.False);
			Assert.That (_driver.PlatformStatus, Is.EqualTo ("Offline"));
			Assert.That (_driver.LastHubRefreshUtc, Is.EqualTo (stamp));
			});
		_transport.FailScheduleRead = false;
		_transport.Rooms = "[{\"id\":4,\"Name\":\"Test room\",\"CurrentSetPoint\":215}]";
		Assert.That (await _driver.RefreshSystemStateAsync (), Is.True, "Offline recovery requires a fresh read even inside the normal cache interval.");
		Assert.Multiple (() =>
			{
			Assert.That (_driver.OnlineIndicatorIsOnline, Is.True);
			Assert.That (room.OnlineIndicatorIsOnline, Is.True);
			Assert.That (room.TargetTemperature, Is.EqualTo (21.5));
			Assert.That (Entities["room_4"], Is.SameAs (room));
			Assert.That (changes, Is.Zero, "Communication loss must not unregister installed room controllers.");
			});
		}

	[Test]
	public async Task LateFailedRead_FromReplacedConnection_DoesNotMarkCurrentDriverOffline ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Test room\"}]");
		var room = Entities["room_4"];
		typeof (WiserHeat.CrestronDriver.WiserPlatformDriver).GetMethod ("SetOnline", Private).Invoke (_driver, new object[] { true });
		_transport.HoldNextDomain = true;
		_transport.FailScheduleRead = true;
		var read = _driver.RefreshSystemStateAsync (true);
		await TestSupport.Complete (_transport.Entered.Task);
		Set ("_connectionGeneration", Field<long> ("_connectionGeneration") + 1);
		_transport.Release.TrySetResult (true);
		Assert.That (await read, Is.False);
		Assert.That (_driver.OnlineIndicatorIsOnline, Is.True);
		Assert.That (room.OnlineIndicatorIsOnline, Is.True);
		}
	}