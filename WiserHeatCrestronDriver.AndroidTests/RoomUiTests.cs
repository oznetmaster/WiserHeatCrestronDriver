// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Net;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed record RoomBinding (int DeviceId, string RoomName, string PageTitle)
		{
		public string? ManagedAlias { get; init; }
		}
	private bool _roomStatePreserved = true;
	private static readonly string[] _roomStateKeys = ["targetTemperature", "boostStateLabel", "selectedScheduleId", "selectedScheduleName", "scheduleStatusLabel"];
	private static readonly string[] _dayLabels = ["    Sunday    ", "    Monday    ", "   Tuesday    ", "Wednesday", "   Thursday   ", "    Friday    ", "   Saturday   "];
	private static AndroidSelector Text (string value) => new (AndroidSelectorKind.Text, value);

	[Test]
	public async Task RoomScheduleSelectorsMatchFreshStateAndReturnHome ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		var bindings = _settings!.Rooms ?? [];
		if (bindings.Length == 0 || bindings.Any (room => room == null || room.DeviceId <= 0 || string.IsNullOrWhiteSpace (room.RoomName) || string.IsNullOrWhiteSpace (room.PageTitle)) ||
			bindings.Select (room => room.DeviceId).Distinct ().Count () != bindings.Length)
			throw new InvalidDataException ("Private Wiser UI settings require distinct Rooms bindings with DeviceId, RoomName and PageTitle. No room is chosen automatically.");
		foreach (var binding in bindings)
			{
			using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (12));
			var before = await ReadRoomAsync (binding, timeout.Token);
			string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture);
			await SaveRoomObservationAsync (check + ".before", before, timeout.Token);
			_roomStatePreserved = false;
			Exception? failure = null;
			try
				{
				await _navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, before.Name!, binding.PageTitle, async (pages, token) =>
						{
							await pages.InspectAsync (check + ".thermostat", hierarchy =>
								{
									var scheduleRow = CrestronHomePages.ReadStatusAndButton (hierarchy, "Schedule");
									Assert.That (scheduleRow.Action, Is.EqualTo ("Open"));
									Assert.That (scheduleRow.Enabled, Is.True);
								}, token);
							await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), token);
							var schedule = await ReadRoomAsync (binding, token);
							var labels = schedule.PropertyValues["selectedScheduleOptions"].EnumerateArray ().Select (value => value.GetProperty ("label").GetProperty ("text").GetString ()!).ToArray ();
							await SaveRoomObservationAsync (check + ".schedule", schedule, token);
							await pages.InspectAsync (check + ".schedule-page", hierarchy =>
								{
									var control = CrestronHomePages.ReadStatusAndButton (hierarchy, "Schedule Control");
									Assert.That (control.Status, Is.EqualTo (Label (schedule.PropertyValues["scheduleStatusLabel"])).IgnoreCase);
									hierarchy.RequireUnique (Text ("SELECT SCHEDULE"));
								}, token);
							await pages.InspectSelectionAsync (check + ".schedules", Text ("SELECT SCHEDULE"), labels, schedule.PropertyValues["selectedScheduleName"].GetString ()!, token);
							await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), token);
							var editor = await ReadRoomAsync (binding, token);
							await SaveRoomObservationAsync (check + ".editor", editor, token);
							await pages.InspectAsync (check + ".editor-page", hierarchy =>
								{
									hierarchy.RequireUnique (Text ("DAY"));
									hierarchy.RequireUnique (Text ("TIME 1"));
									hierarchy.RequireUnique (Text ("Save All"));
								}, token);
							string day = _dayLabels.Single (label => label.Trim () == editor.PropertyValues["editSelectedDay"].GetString ());
							await pages.InspectSelectionAsync (check + ".days", Text ("DAY"), _dayLabels, day, token);
							var times = Enumerable.Range (0, 48).Select (index => (index / 2).ToString ("00", CultureInfo.InvariantCulture) + ":" + (index % 2 * 30).ToString ("00", CultureInfo.InvariantCulture)).ToArray ();
							await pages.InspectSelectionAsync (check + ".times", Text ("TIME 1"), times, editor.PropertyValues["editSlot1Time"].GetString ()!, token);
						}, timeout.Token);
				Assert.That (_navigation.HomeRestored, Is.True);
				}
			catch (Exception e) { failure = e; throw; }
			finally
				{
				using var verification = new CancellationTokenSource (TimeSpan.FromMinutes (1));
				try
					{
					var after = await ReadRoomAsync (binding, verification.Token);
					await SaveRoomObservationAsync (check + ".after", after, verification.Token);
					_roomStatePreserved = after.Name == before.Name && after.ParentDeviceId == before.ParentDeviceId && after.LocationId == before.LocationId &&
						_roomStateKeys.All (key => after.PropertyValues[key].GetRawText () == before.PropertyValues[key].GetRawText ());
					await File.WriteAllTextAsync (Path.Combine (_session!.Context.EvidenceDirectory, check + ".preservation.json"), JsonSerializer.Serialize (new
						{
						_session.Context.RunId,
						_session.Context.PackageSha256,
						binding.DeviceId,
						CheckedStatePreserved = _roomStatePreserved,
						HomeRestored = _navigation!.HomeRestored,
						PhysicalCommandsSent = false
						}), verification.Token);
					Assert.That (_roomStatePreserved, Is.True, "Room identity, assignment or checked control state changed during read-only inspection.");
					}
				catch (Exception verificationError) when (failure != null)
					{
					throw new AggregateException ("Room UI inspection and state verification both failed.", failure, verificationError);
					}
				}
			}
		}

	private async Task<DeviceInfo> ReadRoomAsync (RoomBinding binding, CancellationToken token)
		{
		// Long UI traversals can outlast a configuration session's idle lifetime.
		await using var client = await ConfigurationClient.ConnectAsync (new ()
			{
			Host = _settings!.Host,
			CertificateSha256 = _settings.CertificateSha256
			},
			new NetworkCredential (_settings.UserName, _settings.Password), token);
		return await ReadRoomAsync (client, binding, token);
		}

	private async Task<DeviceInfo> ReadRoomAsync (ConfigurationClient client, RoomBinding binding, CancellationToken token)
		{
		var gateway = await client.GetDeviceAsync (_session!.Context.InstalledDriverId, token) ?? throw new InvalidDataException ("The workflow gateway is missing.");
		if (gateway.Model != "Wiser Heat Gateway" || gateway.PropertyValues["cp.driverInformation:version"].GetString () != _session.Context.DriverVersion ||
			gateway.PropertyValues["cp.driverConfiguration:driverLoadingStatus"].GetString () != "Loaded" || !gateway.PropertyValues["onlineIndicator:isOnline"].GetBoolean ())
			throw new InvalidDataException ("The Wiser gateway is no longer the ready workflow candidate.");
		var device = await client.GetDeviceAsync (binding.DeviceId, token) ?? throw new InvalidDataException ("The explicitly bound room device is missing.");
		if (device.ParentDeviceId != gateway.Id || device.Model != "Room Thermostat" || string.IsNullOrWhiteSpace (device.Name) ||
			device.PropertyValues["cp.driverConfiguration:driverLoadingStatus"].GetString () != "Loaded" || !device.PropertyValues["onlineIndicator:isOnline"].GetBoolean ())
			throw new InvalidDataException ("The bound device is not a ready thermostat child of the workflow gateway.");
		var locations = await client.GetLocationsAsync (token);
		if (locations.Count (location => location.Name == binding.RoomName) != 1 || !locations.Any (location => location.Id == device.LocationId && location.Name == binding.RoomName))
			throw new InvalidDataException ("The explicitly bound room location is missing or ambiguous.");
		var devices = await client.GetDevicesAsync (token);
		if (devices.Count (candidate => candidate.LocationId == device.LocationId && candidate.Name == device.Name) != 1)
			throw new InvalidDataException ("The room tile name is ambiguous.");
		return device;
		}

	private Task SaveRoomObservationAsync (string check, DeviceInfo device, CancellationToken token)
		{
		string[] properties = [.. _roomStateKeys, "selectedScheduleOptions", "editSelectedDay", "editSlot1Time"];
		return File.WriteAllTextAsync (Path.Combine (_session!.Context.EvidenceDirectory, check + ".processor.json"), JsonSerializer.Serialize (new
			{
			ObservedUtc = DateTimeOffset.UtcNow,
			_session.Context.RunId,
			_session.Context.PackageSha256,
			_session.Context.DriverVersion,
			device.Id,
			device.Name,
			device.ParentDeviceId,
			device.LocationId,
			Properties = properties.ToDictionary (key => key, key => device.PropertyValues[key]),
			Binding = "Explicit child ID and location under the workflow gateway; unique named tile and verified saved endpoint. Not cryptographic route attestation."
			}), token);
		}
	}