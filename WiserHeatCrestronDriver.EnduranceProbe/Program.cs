// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

using WiserHeatCrestronDriver.EnduranceProbe;

if (args.SequenceEqual (new[] { "--help" }))
	{
	Console.WriteLine ("Wiser read-only endurance producer. Invoked by DevTools with one JSON request on stdin. Requires reviewed binding.json and candidate.pkg in the pinned bundle; private credentials are supplied separately. No commands modify the hub or driver.");
	return 0;
	}
try
	{
	if (args.Length != 0) throw new ArgumentException ("Unsupported arguments.");
	using var inputTimeout = new CancellationTokenSource (TimeSpan.FromSeconds (10));
	var request = JsonSerializer.Deserialize<SubmissionEnduranceProbeRequest> (
		await Input.ReadBoundedAsync (Console.OpenStandardInput (), 1024 * 1024, inputTimeout.Token), Input.Json)
		?? throw new InvalidDataException ("Missing request.");
	async Task<T> Read<T> (string path, CancellationToken token)
		{
		await using var file = File.Open (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		return JsonSerializer.Deserialize<T> (await Input.ReadBoundedAsync (file, 1024 * 1024, token), Input.Json)
			?? throw new InvalidDataException ("Missing private input.");
		}
	var binding = await Read<Binding> (Path.Combine (AppContext.BaseDirectory, "binding.json"), inputTimeout.Token);
	binding.Validate (request);
	using var timeout = new CancellationTokenSource (request.Plan.ProbeTimeout);
	var expected = binding.ReadCandidate (Path.Combine (AppContext.BaseDirectory, "candidate.pkg"));
	var settings = await Read<CredentialSettings> (request.SettingsFile!, timeout.Token);
	await using var source = await RemoteSource.ConnectAsync (binding, settings, timeout.Token);
	var result = await Observation.RunAsync (binding, request.Plan, expected, source, timeout.Token);
	await JsonSerializer.SerializeAsync (Console.OpenStandardOutput (), result, Input.Json, timeout.Token);
	return 0;
	}
catch
	{
	// Request/settings, raw console output and exception messages may contain private data.
	Console.Error.WriteLine ("Wiser endurance observation failed; no passing evidence was produced. Inspect private inputs and connectivity.");
	return 1;
	}