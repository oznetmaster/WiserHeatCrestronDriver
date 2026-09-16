// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using WiserHeatApiV2;

using WiserHeatCrestronDriver.ControlProbe;

// This process only reads the hub. The installed Home driver performs every control/restoration command.
try
	{
	bool capture = args.Length == 5 && args[0] == "capture";
	string[] pairs = capture ? args[1..] : args;
	if (pairs.Length != (capture ? 4 : 8))
		throw new ArgumentException ();
	var options = Enumerable.Range (0, pairs.Length / 2).ToDictionary (i => pairs[i * 2], i => pairs[i * 2 + 1], StringComparer.Ordinal);
	if (options.Keys.Any (k => k is not ("--settings" or "--baseline" or "--request" or "--response")))
		throw new ArgumentException ();
	using var settings = JsonDocument.Parse (File.ReadAllText (options["--settings"]));
	string Setting (string name) => settings.RootElement.EnumerateObject ().Single (p => p.Name.Equals (name, StringComparison.OrdinalIgnoreCase)).Value.GetString ()!;
	string host = Setting ("hubHost").Trim ().ToLowerInvariant ();
	string roomName = Setting ("controlRoomName");
	if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (roomName))
		throw new InvalidDataException ();
	using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (20));
	using var controller = new WiserRestController (new WiserConnection (host, Setting ("secret")));
	var domain = await controller.GetHubDataAsync ("http://" + host + "/data/v2/domain/", cancellationToken: timeout.Token);
	var rooms = (List<Dictionary<string, object>>)domain["Room"];
	var selected = rooms.Single (r => r.TryGetValue ("Name", out var name) && name?.ToString () == roomName);
	JsonElement room = JsonSerializer.SerializeToElement (selected);
	string identity = host + "/room/" + room.GetProperty ("id").GetInt32 ().ToString (System.Globalization.CultureInfo.InvariantCulture);
	if (capture)
		{
		ScheduleObservation.ValidateCapture (room);
		await using var output = new FileStream (options["--baseline"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
		await JsonSerializer.SerializeAsync (output, new
			{
			PhysicalIdentity = identity,
			CapturedUtc = DateTimeOffset.UtcNow,
			Room = room
			}, cancellationToken: timeout.Token);
		return 0;
		}
	using var baseline = JsonDocument.Parse (File.ReadAllText (options["--baseline"]));
	using var request = JsonDocument.Parse (File.ReadAllText (options["--request"]));
	string nonce = request.RootElement.GetProperty ("RequestId").GetString ()!;
	if (!Guid.TryParseExact (nonce, "N", out _) || request.RootElement.GetProperty ("PhysicalIdentity").GetString () != identity
		|| baseline.RootElement.GetProperty ("PhysicalIdentity").GetString () != identity)
		throw new InvalidDataException ();
	var captured = baseline.RootElement.GetProperty ("CapturedUtc").GetDateTimeOffset ();
	if (captured > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - captured > TimeSpan.FromHours (1))
		throw new InvalidDataException ();
	// Retain the exact observation beside private request/response evidence, including
	// refusals, so an unexpected hub state can be diagnosed without another control.
	await using (var observation = new FileStream (options["--response"] + ".room.json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
		await JsonSerializer.SerializeAsync (observation, room, cancellationToken: timeout.Token);
	bool enabled = ScheduleObservation.Read (baseline.RootElement.GetProperty ("Room"), room);
	await using var response = new FileStream (options["--response"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
	await JsonSerializer.SerializeAsync (response, new
		{
		RequestId = nonce,
		PhysicalIdentity = identity,
		Value = enabled,
		RestoreValue = enabled
		}, cancellationToken: timeout.Token);
	return 0;
	}
catch
	{
	Console.Error.WriteLine ("Wiser observation failed; verify private settings, fresh baseline, room identity and unchanged schedule/override state.");
	return 1;
	}