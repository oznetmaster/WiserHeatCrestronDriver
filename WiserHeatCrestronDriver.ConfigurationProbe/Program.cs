// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using CrestronHomeDevTools;

using WiserHeatCrestronDriver.ConfigurationProbe;

try
	{
	if (args.Length != 8)
		throw new ArgumentException ();
	var options = Enumerable.Range (0, 4).ToDictionary (i => args[i * 2], i => args[i * 2 + 1], StringComparer.Ordinal);
	if (options.Keys.Any (k => k is not ("--processor-settings" or "--pair" or "--request" or "--response")))
		throw new ArgumentException ();
	var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
	var pair = JsonSerializer.Deserialize<ReviewedPair> (File.ReadAllText (options["--pair"]), jsonOptions) ?? throw new InvalidDataException ();
	var packages = ConfigurationCompatibility.ValidatePair (pair);
	using var request = JsonDocument.Parse (File.ReadAllText (options["--request"]));
	var r = request.RootElement;
	string nonce = r.GetProperty ("RequestId").GetString ()!;
	int id = r.GetProperty ("DeviceId").GetInt32 ();
	string version = r.GetProperty ("InstalledVersion").GetString ()!;
	if (!Guid.TryParseExact (nonce, "N", out _) || id <= 0 || r.GetProperty ("Model").GetString () != ConfigurationCompatibility.Model
		|| r.GetProperty ("PreviousPackageSha256").GetString ()?.Equals (pair.PreviousSha256, StringComparison.OrdinalIgnoreCase) != true
		|| r.GetProperty ("PreviousVersion").GetString () != packages.Previous.Version || !r.GetProperty ("PreserveCurrentConfiguration").GetBoolean ()
		|| version != packages.Previous.Version && version != packages.Next.Version
		|| r.GetProperty ("Phase").GetString () is not ("BeforeUpdate" or "BeforeRollback" or "AfterRollback" or "RestorationConfirmed"))
		throw new InvalidDataException ();
	using var settings = JsonDocument.Parse (File.ReadAllText (options["--processor-settings"]));
	string Setting (string key) => settings.RootElement.GetProperty (key).GetString ()!;
	using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
	await using var client = await ConfigurationClient.ConnectAsync (new ()
		{
		Host = Setting ("Host"),
		CertificateSha256 = Setting ("CertificateSha256")
		}, new NetworkCredential (Setting ("UserName"), Setting ("Password")), timeout.Token);
	var device = await client.GetDeviceAsync (id, timeout.Token) ?? throw new InvalidDataException ();
	if (device.ParentDeviceId != -6 || device.Model != ConfigurationCompatibility.Model)
		throw new InvalidDataException ();
	var snapshot = await DriverConfigurationInspection.GetAsync (client, id, timeout.Token);
	if (snapshot.Version != version)
		throw new InvalidDataException ();
	string identity = ConfigurationCompatibility.Identity (snapshot);
	await using var output = new FileStream (options["--response"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
	await JsonSerializer.SerializeAsync (output, new
		{
		RequestId = nonce,
		DeviceId = id,
		Model = snapshot.Model,
		InstalledVersion = version,
		PreviousPackageSha256 = pair.PreviousSha256,
		CompatibleWithPreviousVersion = true,
		ConfigurationIdentity = identity
		}, cancellationToken: timeout.Token);
	return 0;
	}
catch
	{
	Console.Error.WriteLine ("Wiser configuration compatibility was not verified. Check the reviewed package pair and complete current configuration privately.");
	return 1;
	}