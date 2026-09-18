// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record RoomPeerSnapshot (int DeviceId, int LocationId, string Name, string PhysicalIdentity,
	ScheduleActivity Activity, string TemperatureUnits, bool ScheduleEnabled, string ScheduleId, double TargetTemperature);

public static class RoomPeerObservation
	{
	public static void RequirePreserved (RoomPeerSnapshot original, RoomPeerSnapshot current)
		{
		foreach (var value in new[] { original, current })
			if (value.DeviceId <= 0 || value.LocationId <= 0 || string.IsNullOrWhiteSpace (value.Name) ||
				string.IsNullOrWhiteSpace (value.PhysicalIdentity) || !Guid.TryParseExact (value.Activity.Epoch, "N", out _) ||
				value.Activity.Completed < 0 || value.Activity.Pending != 0 || !double.IsFinite (value.TargetTemperature) ||
				value.TemperatureUnits is not ("Celsius" or "Fahrenheit"))
				throw new InvalidDataException ("Peer room identity, activity or units are invalid.");
		if (original.DeviceId != current.DeviceId || original.LocationId != current.LocationId || original.Name != current.Name ||
			original.PhysicalIdentity != current.PhysicalIdentity || original.Activity != current.Activity || original.TemperatureUnits != current.TemperatureUnits)
			throw new InvalidDataException ("Peer room changed identity, configuration or command activity.");
		}

	public static bool Matches (ScheduleControlSnapshot hub, RoomPeerSnapshot peer)
		{
		RequirePreserved (peer, peer);
		if (peer.PhysicalIdentity != hub.PhysicalIdentity)
			throw new InvalidDataException ("Peer room does not identify the independently observed physical room.");
		bool enabled = hub.Room.GetProperty ("Mode").GetString () switch
			{ "Auto" => true, "Manual" => false, _ => throw new InvalidDataException ("Unsupported room mode.") };
		int raw = hub.Room.GetProperty ("CurrentSetPoint").GetInt32 ();
		// This mode-cycle test is restricted to normal heating setpoints, never Off or overrides.
		if (raw is < 50 or > 300) throw new InvalidDataException ("Unsupported room target.");
		double expected = peer.TemperatureUnits == "Fahrenheit" ? Math.Round (raw * 0.18 + 32, 1) : raw / 10.0;
		return hub.HomeEnabled == enabled && peer.ScheduleEnabled == enabled &&
			peer.ScheduleId == hub.Room.GetProperty ("ScheduleId").GetInt32 ().ToString (CultureInfo.InvariantCulture) &&
			Math.Abs (peer.TargetTemperature - expected) < 0.00001;
		}
	}