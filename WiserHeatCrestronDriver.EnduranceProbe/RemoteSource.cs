// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Net;
using System.Globalization;
using System.Text.Json;

using CrestronHomeDevTools;

using Renci.SshNet;

namespace WiserHeatCrestronDriver.EnduranceProbe;

internal sealed class RemoteSource : IObservationSource
	{
	private readonly Binding _binding;
	private readonly NetworkCredential _credential;
	private readonly ProcessorOperationLease _lease;
	private readonly ConfigurationClient _configuration;
	private readonly SftpClient _sftp;
	private readonly HttpClient _http;

	private RemoteSource (Binding binding, NetworkCredential credential, ProcessorOperationLease lease,
		ConfigurationClient configuration, SftpClient sftp, HttpClient http)
		{
		_binding = binding; _credential = credential; _lease = lease;
		_configuration = configuration; _sftp = sftp; _http = http;
		}

	internal static async Task<RemoteSource> ConnectAsync (Binding binding, CredentialSettings settings, CancellationToken token)
		{
		if (string.IsNullOrWhiteSpace (settings.UserName) || string.IsNullOrEmpty (settings.Password) || string.IsNullOrEmpty (settings.HubSecret))
			throw new InvalidDataException ("Missing private credentials.");
		var credential = new NetworkCredential (settings.UserName, settings.Password);
		// Attach only. The monitor owns acquisition, persistence and eventual release.
		var lease = await ProcessorOperationLease.ResumeAsync (binding.ProcessorHost, credential, binding.SshFingerprint, binding.ApprovedPlan.ReservationId, token);
		ConfigurationClient? configuration = null;
		SftpClient? sftp = null;
		HttpClient? http = null;
		try
			{
			configuration = await ConfigurationClient.ConnectAsync (new () { Host = binding.ProcessorHost, CertificateSha256 = binding.CertificateSha256 }, credential, token);
			sftp = new SftpClient (binding.ProcessorHost, credential.UserName, credential.Password);
			sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10); sftp.OperationTimeout = TimeSpan.FromSeconds (15);
			sftp.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == binding.SshFingerprint;
			await sftp.ConnectAsync (token);
			http = new (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
			http.DefaultRequestHeaders.Add ("SECRET", settings.HubSecret);
			return new (binding, credential, lease, configuration, sftp, http);
			}
		catch
			{
			if (configuration != null) await configuration.DisposeAsync ();
			sftp?.Dispose (); http?.Dispose (); lease.Dispose ();
			throw;
			}
		}

	public Task VerifyOwnerAsync (CancellationToken token) => _lease.VerifyAfterReconnectAsync (_binding.ProcessorHost, token);
	public Task<ProcessorUptimeSnapshot> ReadUptimeAsync (CancellationToken token)
		=> ProcessorUptime.ReadAsync (_binding.ProcessorHost, _credential, _binding.SshFingerprint, TimeSpan.FromSeconds (20), token);

	public async Task<IReadOnlyDictionary<string, string>> ReadPayloadAsync (CancellationToken token)
		{
		string root = _binding.InstalledDirectory;
		var result = new Dictionary<string, string> (StringComparer.Ordinal);
		var pending = new Stack<string> ();
		pending.Push (root);
		int entries = 0;
		long total = 0;
		while (pending.TryPop (out var directory))
			{
			await foreach (var entry in _sftp.ListDirectoryAsync (directory, token))
				{
				if (entry.Name is "." or "..") continue;
				if (++entries > 256 || entry.IsSymbolicLink || !entry.FullName.StartsWith (root + "/", StringComparison.Ordinal) ||
					!Binding.SafeRelative (entry.FullName[(root.Length + 1)..]))
					throw new InvalidDataException ("Unexpected active payload layout.");
				if (entry.IsDirectory) { pending.Push (entry.FullName); continue; }
				if (!entry.IsRegularFile || entry.Length > Input.MaximumFileBytes || (total += entry.Length) > Input.MaximumPayloadBytes)
					throw new InvalidDataException ("Unexpected active payload size or type.");
				await using var input = await _sftp.OpenAsync (entry.FullName, FileMode.Open, FileAccess.Read, token);
				result.Add (entry.FullName[(root.Length + 1)..], await Input.HashBoundedAsync (input, Input.MaximumFileBytes, token));
				}
			}
		return result;
		}

	public async Task<DriverState> ReadDriverAsync (CancellationToken token)
		{
		var driver = await _configuration.GetDeviceAsync (_binding.DriverId, token) ?? throw new InvalidDataException ("Driver instance missing.");
		var configuration = await DriverConfigurationInspection.GetAsync (_configuration, _binding.DriverId, token);
		if (configuration.Version != driver.PropertyValues["cp.driverInformation:version"].GetString () || configuration.IsConfigured != true)
			throw new InvalidDataException ("Driver configuration identity changed.");
		var host = configuration.Items.Single (item => item.Id == "_Host_");
		if (host.Masked || !host.HasCurrentValue || host.CurrentValue?.ValueKind != JsonValueKind.String)
			throw new InvalidDataException ("Driver hub binding could not be read.");
		bool Flag (string name) => driver.PropertyValues[name].GetBoolean ();
		return new (driver.Id, driver.ParentDeviceId, driver.Name, driver.Model, driver.LocationId,
			driver.PropertyValues["cp.driverInformation:version"].GetString ()!, host.CurrentValue.Value.GetString ()!,
			driver.PropertyValues["cp.driverConfiguration:driverLoadingStatus"].GetString () == "Loaded",
			Flag ("readyIndicator:isReady"), Flag ("onlineIndicator:isOnline"), Flag ("hotWaterVisible"), Flag ("awayModeVisible"),
			Flag ("hotWaterIsOn"), Flag ("awayModeIsEnabled"),
			ParseRefreshTimestamp (driver.PropertyValues["lastHubRefreshUtc"].GetString ()!), DateTimeOffset.UtcNow,
			driver.PropertyValues["driverLifetimeId"].GetString ()!);
		}

	internal static DateTimeOffset ParseRefreshTimestamp (string value)
		{
		if (value == null || !value.StartsWith ("utc:", StringComparison.Ordinal))
			throw new FormatException ("Missing UTC diagnostic tag.");
		var result = DateTimeOffset.ParseExact (value.AsSpan (4), "O", CultureInfo.InvariantCulture);
		if (result.Offset != TimeSpan.Zero) throw new FormatException ("The diagnostic must use UTC.");
		return result;
		}

	public async Task<HubState> ReadHubAsync (CancellationToken token)
		{
		var uri = new UriBuilder ("http", _binding.HubHost, 80, "/data/v2/domain/").Uri;
		using var response = await _http.GetAsync (uri, HttpCompletionOption.ResponseHeadersRead, token);
		response.EnsureSuccessStatusCode ();
		await using var stream = await response.Content.ReadAsStreamAsync (token);
		using var data = JsonDocument.Parse (await Input.ReadBoundedAsync (stream, 4 * 1024 * 1024, token));
		return ParseHub (data.RootElement, _binding.HotWaterId);
		}

	internal static HubState ParseHub (JsonElement domain, int hotWaterId)
		{
		var controller = domain.GetProperty ("Device").EnumerateArray ().Single (item =>
			item.TryGetProperty ("ProductType", out var type) && type.GetString () == "Controller");
		var hotWater = domain.GetProperty ("HotWater").EnumerateArray ().Single ();
		if (hotWater.GetProperty ("id").GetInt32 () != hotWaterId) throw new InvalidDataException ("The selected hot-water channel changed.");
		string state = hotWater.GetProperty ("WaterHeatingState").GetString ()!;
		string mode = domain.GetProperty ("System").GetProperty ("OverrideType").GetString ()!;
		if (state is not ("On" or "Off") || mode is not ("None" or "Away"))
			throw new InvalidDataException ("Unrecognized hub state.");
		return new (controller.GetProperty ("UUID").GetString ()!, controller.GetProperty ("ModelIdentifier").GetString ()!,
			hotWaterId, state == "On", mode == "Away");
		}

	public async ValueTask DisposeAsync ()
		{
		try { await _configuration.DisposeAsync (); }
		finally { _sftp.Dispose (); _http.Dispose (); _lease.Dispose (); }
		}
	}