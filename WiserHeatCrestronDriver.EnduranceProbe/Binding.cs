// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using CrestronHomeDevTools;

namespace WiserHeatCrestronDriver.EnduranceProbe;

internal sealed record CredentialSettings (string UserName, string Password, string HubSecret);
internal sealed record BootWindow (DateTimeOffset EarliestUtc, DateTimeOffset LatestUtc, TimeSpan ClockTolerance);
internal sealed record Binding (int SchemaVersion, SubmissionEndurancePlan ApprovedPlan,
	string ProcessorHost, string CertificateSha256, string SshFingerprint,
	int DriverId, string DriverName, int LocationId, string DriverKey, string DriverVersion,
	string HubHost, string HubUuid, string HubModel, int HotWaterId, BootWindow Boot, TimeSpan MaximumHubRefreshAge, string DriverLifetimeId)
	{
	internal const string Model = "Wiser Heat Gateway";
	internal const string Guid = "8f153bae-6a59-44b5-90bc-c73f635a4d90";
	internal string InstalledDirectory => $"/user/Data/UsedThirdPartyDrivers/{DriverKey}/{DriverVersion}";
	internal string BootIdentity => "reviewed-start-window:" + Convert.ToHexString (SHA256.HashData (JsonSerializer.SerializeToUtf8Bytes (Boot, Input.Json)));

	internal void Validate (SubmissionEnduranceProbeRequest request)
		{
		ArgumentNullException.ThrowIfNull (request);
		if (SchemaVersion != 1 || request.SchemaVersion != 1 || ApprovedPlan == null || request.Plan == null || Boot == null)
			throw new InvalidDataException ("Unsupported endurance contract.");
		SubmissionEndurance.ValidatePlan (request.Plan);
		// ProducerId is calculated after this binding joins the pinned bundle. Avoid a recursive hash dependency.
		if (ApprovedPlan.ProducerId != "" || JsonSerializer.Serialize (ApprovedPlan, Input.Json) !=
			JsonSerializer.Serialize (request.Plan with { ProducerId = "" }, Input.Json))
			throw new InvalidDataException ("The request differs from the reviewed endurance plan.");
		if (!System.Guid.TryParseExact (request.Plan.ReservationId, "N", out _) ||
			request.Plan.ProbeTimeout > TimeSpan.FromMinutes (2) || request.Plan.ProbeTimeout < TimeSpan.FromSeconds (10) ||
			Uri.CheckHostName (ProcessorHost) == UriHostNameType.Unknown || Uri.CheckHostName (HubHost) == UriHostNameType.Unknown ||
			!Regex.IsMatch (CertificateSha256 ?? "", "^[A-Fa-f0-9]{64}$") || string.IsNullOrWhiteSpace (SshFingerprint) ||
			DriverId <= 0 || LocationId <= 0 || string.IsNullOrWhiteSpace (DriverName) ||
			!Regex.IsMatch (DriverKey ?? "", "^[a-z0-9][a-z0-9_.-]{0,127}$") || DriverKey!.Contains ("..", StringComparison.Ordinal) ||
			!Regex.IsMatch (DriverVersion ?? "", @"^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$") ||
			string.IsNullOrWhiteSpace (HubUuid) || string.IsNullOrWhiteSpace (HubModel) || HotWaterId < 0 ||
			Boot.EarliestUtc == default || Boot.LatestUtc < Boot.EarliestUtc || Boot.LatestUtc - Boot.EarliestUtc > TimeSpan.FromSeconds (5) ||
			Boot.ClockTolerance < TimeSpan.Zero || Boot.ClockTolerance > TimeSpan.FromSeconds (5) ||
			!System.Guid.TryParseExact (DriverLifetimeId, "N", out _) || MaximumHubRefreshAge < TimeSpan.FromSeconds (30) || MaximumHubRefreshAge > TimeSpan.FromMinutes (5))
			throw new InvalidDataException ("Invalid reviewed endurance bindings.");
		if (request.SettingsFile == null || !Path.IsPathFullyQualified (request.SettingsFile))
			throw new InvalidDataException ("An absolute private credential settings path is required.");
		}

	internal Dictionary<string, string> ReadCandidate (string path)
		{
		using var input = File.Open (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (Convert.ToHexString (SHA256.HashData (input)) != ApprovedPlan.Identity.PackageSha256.ToUpperInvariant ())
			throw new InvalidDataException ("Candidate package changed.");
		var info = DriverDeployment.Inspect (path);
		if (!string.Equals (info.DriverId, Guid, StringComparison.OrdinalIgnoreCase) || info.Model != Model || info.Version != DriverVersion)
			throw new InvalidDataException ("Candidate is not the reviewed Wiser version.");
		input.Position = 0;
		using var archive = new ZipArchive (input, ZipArchiveMode.Read, true);
		return ReadEntries (archive);
		}

	internal static Dictionary<string, string> ReadEntries (ZipArchive archive)
		{
		if (archive.Entries.Count is 0 or > 128) throw new InvalidDataException ("Invalid payload entry count.");
		var files = new Dictionary<string, string> (StringComparer.Ordinal);
		var unique = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		long total = 0;
		foreach (var entry in archive.Entries)
			{
			string name = entry.FullName.Replace ('\\', '/');
			if (!SafeRelative (name) || !unique.Add (name) || entry.Length > Input.MaximumFileBytes ||
				(total += entry.Length) > Input.MaximumPayloadBytes)
				throw new InvalidDataException ("Unsafe, duplicate or oversized candidate entry.");
			using var content = entry.Open ();
			files.Add (name, Input.HashBoundedAsync (content, Input.MaximumFileBytes, default).GetAwaiter ().GetResult ());
			}
		return files;
		}

	internal static bool SafeRelative (string path) => path.Length is > 0 and <= 512 && !path.Contains ('\\') &&
		!path.Contains (':') && !path.Any (char.IsControl) && path.Split ('/').All (part => part is not ("" or "." or ".."));
	}

internal static class Input
	{
	internal const int MaximumFileBytes = 16 * 1024 * 1024;
	internal const int MaximumPayloadBytes = 64 * 1024 * 1024;
	internal static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		Converters = { new JsonStringEnumConverter () }
		};
	internal static async Task<byte[]> ReadBoundedAsync (Stream input, int limit, CancellationToken token)
		{
		using var output = new MemoryStream ();
		var buffer = new byte[8192];
		int count;
		while ((count = await input.ReadAsync (buffer, token)) > 0)
			{
			if (output.Length + count > limit) throw new InvalidDataException ("Input exceeds its size limit.");
			output.Write (buffer, 0, count);
			}
		return output.ToArray ();
		}
	internal static async Task<string> HashBoundedAsync (Stream input, int limit, CancellationToken token)
		{
		using var hash = IncrementalHash.CreateHash (HashAlgorithmName.SHA256);
		var buffer = new byte[8192];
		int total = 0, count;
		while ((count = await input.ReadAsync (buffer, token)) > 0)
			{
			if (count > limit - total) throw new InvalidDataException ("Input exceeds its size limit.");
			total += count;
			hash.AppendData (buffer, 0, count);
			}
		return Convert.ToHexString (hash.GetHashAndReset ());
		}
	}