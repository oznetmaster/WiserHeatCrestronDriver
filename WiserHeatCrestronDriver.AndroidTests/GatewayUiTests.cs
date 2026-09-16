// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.AndroidTests;

/// <summary>Read-only UI comparisons. Never sends a heating, away or configuration command.</summary>
[TestFixture, NonParallelizable]
public sealed class GatewayUiTests
	{
	private AndroidWorkflowSession? _session;
	private CrestronHomeNavigation? _navigation;
	private ConfigurationClient? _processor;
	private sealed record Settings (string Host, string UserName, string Password, string CertificateSha256);
	private static readonly string[] StateKeys = ["hotWaterVisible", "hotWaterStateLabel", "hotWaterActionLabel", "hotWaterActionEnabled", "awayModeVisible", "awayModeStateLabel", "awayModeActionLabel", "awayModeActionEnabled"];

	[OneTimeSetUp]
	public async Task ConnectReadOnly ()
		{
		if (string.IsNullOrWhiteSpace (Environment.GetEnvironmentVariable (AndroidWorkflowSession.CONTEXT_VARIABLE)))
			Assert.Ignore ("Android UI tests require an opted-in processor workflow holding both reservations.");
		var path = Environment.GetEnvironmentVariable ("CRESTRON_HOME_WISER_UI_SETTINGS");
		if (string.IsNullOrWhiteSpace (path) || !Path.IsPathFullyQualified (path))
			throw new InvalidDataException ("Provide the absolute private Wiser UI settings path.");
		var settings = JsonSerializer.Deserialize<Settings> (File.ReadAllText (path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Private Wiser UI settings are missing.");
		if (string.IsNullOrWhiteSpace (settings.Host) || string.IsNullOrWhiteSpace (settings.UserName) || string.IsNullOrWhiteSpace (settings.Password) || string.IsNullOrWhiteSpace (settings.CertificateSha256))
			throw new InvalidDataException ("Private Wiser UI settings are incomplete.");
		var context = AndroidWorkflowSession.Read<AndroidRunContext> (Environment.GetEnvironmentVariable (AndroidWorkflowSession.CONTEXT_VARIABLE)!);
		if (settings.Host != context.ProcessorAddress || !Guid.TryParse (context.DriverGuid, out var guid) || guid != Guid.Parse ("8f153bae-6a59-44b5-90bc-c73f635a4d90"))
			throw new InvalidDataException ("The workflow is not bound to this Wiser gateway/processor.");
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (3));
		_session = await AndroidWorkflowSession.OpenFromEnvironmentAsync (timeout.Token);
		_navigation = new (_session);
		_processor = await ConfigurationClient.ConnectAsync (new () { Host = settings.Host, CertificateSha256 = settings.CertificateSha256 }, new NetworkCredential (settings.UserName, settings.Password), timeout.Token);
		_ = await ReadGatewayAsync (timeout.Token);
		await _navigation.VerifySavedEndpointAsync ("wiser.connection", context.Profile.LocalPort, timeout.Token);
		}

	private async Task<DeviceInfo> ReadGatewayAsync (CancellationToken token)
		{
		var context = _session!.Context;
		var device = await _processor!.GetDeviceAsync (context.InstalledDriverId, token) ?? throw new InvalidDataException ("The selected gateway is no longer installed.");
		if (device.Model != "Wiser Heat Gateway" || string.IsNullOrWhiteSpace (device.Name) ||
			device.PropertyValues["cp.driverInformation:version"].GetString () != context.DriverVersion ||
			device.PropertyValues["cp.driverConfiguration:driverLoadingStatus"].GetString () != "Loaded" ||
			!device.PropertyValues["onlineIndicator:isOnline"].GetBoolean ())
			throw new InvalidDataException ("The installed Wiser gateway does not match the ready workflow candidate.");
		var inventory = await _processor.GetDevicesAsync (token);
		if (inventory.Count (d => d.Name == device.Name) != 1)
			throw new InvalidDataException ("The gateway's tile name is ambiguous.");
		return device;
		}

	private static string Label (JsonElement value) => value.GetString () switch
		{
		"^HotWaterOnLabel" => "On", "^HotWaterOffLabel" => "Off",
		"^HotWaterTurnOnLabel" => "Hot Water On", "^HotWaterTurnOffLabel" => "Hot Water Off",
		"^AwayEnabledLabel" => "Enabled", "^AwayDisabledLabel" => "Disabled",
		"^AwayEnableActionLabel" => "Enable Away", "^AwayDisableActionLabel" => "Disable Away",
		_ => throw new InvalidDataException ("An unexpected status needs explicit UI translation support.")
		};

	[TestCase (1), TestCase (2)]
	public async Task GatewayControlsMatchFreshProcessorStateAndReturnHome (int repetition)
		{
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (3));
		var before = await ReadGatewayAsync (timeout.Token);
		string check = $"wiser.gateway-{repetition}";
		var values = StateKeys.ToDictionary (key => key, key => before.PropertyValues[key]);
		await File.WriteAllTextAsync (Path.Combine (_session!.Context.EvidenceDirectory, check + ".processor.json"), JsonSerializer.Serialize (new
			{
			ObservedUtc = DateTimeOffset.UtcNow, before.Id, before.Name, before.Model, _session.Context.DriverVersion, Properties = values,
			Binding = "Unique management inventory name and saved Android endpoint; not active-route proof."
			}), timeout.Token);
		await _navigation!.InspectHomeExtensionAsync (check, before.Name!, "Wiser Heat Options", hierarchy =>
			{
			foreach (var control in new[] { (Label: "Hot Water", Prefix: "hotWater"), (Label: "Away Mode", Prefix: "awayMode") })
				{
				Assert.That (values[control.Prefix + "Visible"].GetBoolean (), Is.True, "This fixture requires the configured gateway capability to be visible.");
				var row = CrestronHomePages.ReadStatusAndButton (hierarchy, control.Label);
				Assert.That (row.Status, Is.EqualTo (Label (values[control.Prefix + "StateLabel"])).IgnoreCase, control.Label);
				Assert.That (row.Action, Is.EqualTo (Label (values[control.Prefix + "ActionLabel"])), control.Label);
				Assert.That (row.Enabled, Is.EqualTo (values[control.Prefix + "ActionEnabled"].GetBoolean ()), control.Label);
				}
			}, timeout.Token);
		var after = await ReadGatewayAsync (timeout.Token);
		foreach (var key in StateKeys)
			Assert.That (after.PropertyValues[key].GetRawText (), Is.EqualTo (values[key].GetRawText ()), "Gateway state changed during read-only inspection: " + key);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[OneTimeTearDown]
	public async Task RestoreAndDisconnect ()
		{
		try
			{
			if (_session == null) return;
			bool restored = false;
			try
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
				await _navigation!.RestoreHomeAsync (cleanup.Token);
				restored = _navigation.HomeRestored;
				}
			finally { _session.Complete (restorationConfirmed: restored); }
			}
		finally { if (_processor != null) await _processor.DisposeAsync (); }
		}
	}