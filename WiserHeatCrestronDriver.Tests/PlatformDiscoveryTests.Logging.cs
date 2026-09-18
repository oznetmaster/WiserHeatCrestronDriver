// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Linq;
using System.Threading.Tasks;

using Crestron.DeviceDrivers.EntityModel.Logging;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	[Test]
	public async Task UnchangedPolling_DoesNotRepeatDiagnosticMessages ()
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Test room\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Schedule\"}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		Entities["room_4"].StartPolling ();
		var logger = (TestLogger)_logger.AppLogger;
		while (logger.Entries.TryDequeue (out _)) { }
		for (int iteration = 0; iteration < 20; iteration++)
			await Refresh (rooms);
		Assert.That (logger.Entries, Is.Empty, "An unchanged hub snapshot must not fill the processor log, even in Debug builds.");
		Assert.That (Entities["room_4"].SelectedScheduleId, Is.EqualTo ("7"));
		}

	[Test]
	public async Task ScheduleChange_IsLoggedOnceAndErrorsRemainVisible ()
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Test room\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Original\"}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		Entities["room_4"].StartPolling ();
		var logger = (TestLogger)_logger.AppLogger;
		while (logger.Entries.TryDequeue (out _)) { }
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Renamed\"}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		await Refresh (rooms);
		Assert.That (logger.Entries.Count (entry => entry.Message.Contains ("Schedule selector changed")), Is.EqualTo (1));
		_driver.ApiFactory = (_, _, _) => throw new System.InvalidOperationException ("Synthetic connection failure.");
		await TestSupport.Complete (Connect ());
		Assert.That (logger.Entries.Any (entry => entry.Level == LogEntryLevel.Error), Is.True,
			"Suppressing routine polling messages must not hide a connection failure.");
		}
	}