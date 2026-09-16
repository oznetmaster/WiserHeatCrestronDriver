// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CrestronHomeDevTools;

namespace WiserHeatCrestronDriver.ConfigurationProbe;

public sealed record ReviewedPair (string PreviousPackage, string PreviousSha256, string NextPackage, string NextSha256, string Review);

// Wiser stores its driver configuration in the SDK's seven persistent items. Room schedules live on the hub.
// A pair still needs source review: identical metadata does not prove that code has no migration behavior.
public static class ConfigurationCompatibility
	{
	public const string Model = "Wiser Heat Gateway";
	private const string DriverId = "8f153bae-6a59-44b5-90bc-c73f635a4d90";
	private static readonly string[] ItemIds = ["_Host_", "HubSecret", "TemperatureUnits", "BoostDelta", "BoostDurationMinutes", "EnableWholeHouseHotWater", "AllowAwayMode"];
	public static (DriverPackageInfo Previous, DriverPackageInfo Next) ValidatePair (ReviewedPair pair)
		{
		if (string.IsNullOrWhiteSpace (pair.Review))
			throw new InvalidDataException ("A reviewed exact package pair is required.");
		var previous = Inspect (pair.PreviousPackage, pair.PreviousSha256);
		var next = Inspect (pair.NextPackage, pair.NextSha256);
		if (Version.Parse (previous.Info.Version) >= Version.Parse (next.Info.Version)
			|| !JsonElement.DeepEquals (previous.Configuration, next.Configuration))
			throw new InvalidDataException ("The reviewed pair must preserve the configuration schema and upgrade version.");
		return (previous.Info, next.Info);
		}
	private static (DriverPackageInfo Info, JsonElement Configuration) Inspect (string path, string hash)
		{
		if (!Path.IsPathFullyQualified (path) || hash.Length != 64 || !hash.All (Uri.IsHexDigit))
			throw new InvalidDataException ();
		using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (!Convert.ToHexString (SHA256.HashData (input)).Equals (hash, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException ("Package hash changed.");
		// Keep the verified bytes locked while inspecting both metadata and configuration.
		var info = DriverDeployment.Inspect (path);
		if (info.DriverId != DriverId || info.Model != Model)
			throw new InvalidDataException ("Unexpected driver family.");
		input.Position = 0;
		using var zip = new ZipArchive (input, ZipArchiveMode.Read, true);
		var entry = zip.Entries.Single (e => !e.FullName.Contains ('/') && !e.FullName.Contains ('\\') && e.FullName.EndsWith (".dat", StringComparison.OrdinalIgnoreCase));
		using var stream = entry.Open ();
		using var document = JsonDocument.Parse (stream);
		var configuration = document.RootElement.GetProperty ("configuration");
		var items = configuration.GetProperty ("items").EnumerateArray ().ToArray ();
		if (!items.Select (i => i.GetProperty ("id").GetString ()).Order ().SequenceEqual (ItemIds.Order ()))
			throw new InvalidDataException ("Unsupported configuration schema.");
		return (info, configuration.Clone ());
		}
	public static string Identity (DriverConfigurationSnapshot snapshot)
		{
		if (snapshot.Model != Model || snapshot.IsConfigured != true || !snapshot.ItemsAvailable
			|| !snapshot.Items.Select (i => i.Id).Order ().SequenceEqual (ItemIds.Order ())
			|| snapshot.Items.Any (i => i.Masked || !i.HasCurrentValue || i.CurrentValue?.ValueKind != JsonValueKind.String))
			throw new InvalidDataException ("Complete current configuration is required; masked or missing values cannot be inferred.");
		var values = snapshot.Items.ToDictionary (i => i.Id, i => i.CurrentValue!.Value.GetString ()!);
		if (string.IsNullOrWhiteSpace (values["_Host_"]) || Uri.CheckHostName (values["_Host_"]) == UriHostNameType.Unknown
			|| string.IsNullOrWhiteSpace (values["HubSecret"]) || values["HubSecret"].All (c => c is '*' or '•')
			|| values["TemperatureUnits"] is not ("Celsius" or "Fahrenheit")
			|| !double.TryParse (values["BoostDelta"], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta) || !double.IsFinite (delta) || delta is <= 0 or > 10
			|| !int.TryParse (values["BoostDurationMinutes"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration) || duration is < 1 or > 1440
			|| !bool.TryParse (values["EnableWholeHouseHotWater"], out _) || !bool.TryParse (values["AllowAwayMode"], out _))
			throw new InvalidDataException ("Current configuration is not readable by the reviewed driver contract.");
		// Include the secret in the private identity hash so changed credentials cannot pass as unchanged configuration.
		var canonical = JsonSerializer.Serialize (snapshot.Items.OrderBy (i => i.Id, StringComparer.Ordinal).Select (i => new { i.Id, i.ValueType, i.Required, i.ReadOnly, Value = values[i.Id] }));
		return Convert.ToHexString (SHA256.HashData (Encoding.UTF8.GetBytes (canonical)));
		}
	}