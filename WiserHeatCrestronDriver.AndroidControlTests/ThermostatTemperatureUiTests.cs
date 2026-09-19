// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using static WiserHeatCrestronDriver.AndroidTests.ThermostatTemperatureRendering;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool AllowThermostatTemperatureObservation { get; init; }
		}

	[Test, Category ("LiveReadOnly")]
	public async Task NativeThermostatTemperaturesMatchHubAndReturnHome ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (!_settings!.AllowThermostatTemperatureObservation)
			Assert.Ignore ("Enable AllowThermostatTemperatureObservation for independent, read-only thermostat temperature inspection.");
		if (_settings.ControlRooms.Length == 0 || _settings.ControlRooms.Select (r => r.DeviceId).Distinct ().Count () != _settings.ControlRooms.Length ||
			string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("Explicit room bindings and absolute private hub settings are required.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret))
			throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		string host = hub.HubHost.Trim ().ToLowerInvariant ();
		foreach (var control in _settings.ControlRooms)
			{
			var binding = _settings.Rooms.Single (room => room.DeviceId == control.DeviceId);
			using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (5));
			var original = await ReadRoomAsync (binding, timeout.Token);
			string units = original.PropertyValues["temperatureUnits"].GetString () switch
				{
				"Celsius" => "Celsius",
				"Fahrenheit" => "Fahrenheit",
				_ => throw new InvalidDataException ("The observed thermostat must use Celsius or Fahrenheit.")
				};
			string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".temperatures";
			async Task<HubTemperatures> ReadHub (CancellationToken token)
				{
				using var response = await http.GetAsync ("http://" + host + "/data/v2/domain/", token);
				response.EnsureSuccessStatusCode ();
				using var data = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
				var room = data.RootElement.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName);
				return new (room.GetProperty ("id").GetInt32 (), room.GetProperty ("CalculatedTemperature").GetInt32 (), room.GetProperty ("CurrentSetPoint").GetInt32 ());
				}
			void VerifyIdentity (DeviceInfo device, HubTemperatures observed)
				{
				if (device.Id != original.Id || device.Name != original.Name || device.ParentDeviceId != original.ParentDeviceId || device.LocationId != original.LocationId ||
					device.PropertyValues["controlDeviceId"].GetString () != host + "/room/" + observed.Id.ToString (CultureInfo.InvariantCulture) ||
					device.PropertyValues["temperatureUnits"].GetString () != units)
					throw new InvalidDataException ("The observed thermostat identity or temperature units changed.");
				}
			await _navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, original.Name!, binding.PageTitle, async (pages, token) =>
				{
				// Poll only observations while allowing a normal hub refresh; never repeat navigation or send a temperature command.
				using var convergence = CancellationTokenSource.CreateLinkedTokenSource (token);
				convergence.CancelAfter (TimeSpan.FromSeconds (90));
				for (int attempt = 1; ; attempt++)
					{
					var firstHub = await ReadHub (convergence.Token);
					var first = await ReadRoomAsync (binding, convergence.Token);
					VerifyIdentity (first, firstHub);
					RenderedTemperatures? shown = null;
					await pages.InspectAsync (check + ".sample-" + attempt.ToString (CultureInfo.InvariantCulture), hierarchy =>
						{
							hierarchy.RequireUnique (CrestronHomePages.Resource ("customdevice_thermostat_gauge"));
							var target = CrestronHomePages.Resource ("statusLabels_value") with { AncestorResourceId = CrestronHomePages.ResourcePrefix + "customdevice_thermostat_heatSetpoint" };
							shown = new (hierarchy.RequireUnique (CrestronHomePages.Resource ("customdevice_thermostat_currentTemperature")).Text,
								hierarchy.RequireUnique (CrestronHomePages.Resource ("customdevice_thermostat_currentTempLabel")).Text, hierarchy.RequireUnique (target).Text);
							}, convergence.Token);
					var last = await ReadRoomAsync (binding, convergence.Token);
					var lastHub = await ReadHub (convergence.Token);
					VerifyIdentity (last, lastHub);
					bool stable = firstHub == lastHub && new[] { "currentTemperature", "targetTemperature", "currentTemperatureLabel", "temperatureUnits" }
						.All (key => JsonElement.DeepEquals (first.PropertyValues[key], last.PropertyValues[key]));
					// These ranges exclude the hub's unavailable/error sentinels. An unavailable reading cannot prove temperature rendering.
					bool ordinary = firstHub.Current is >= 0 and <= 600 && firstHub.Target is >= 50 and <= 300;
					bool matches = stable && ordinary && ThermostatTemperatureRendering.Matches (firstHub,
						new ProcessorTemperatures (units, last.PropertyValues["currentTemperature"].GetDouble (),
							last.PropertyValues["targetTemperature"].GetDouble (), last.PropertyValues["currentTemperatureLabel"].GetString ()!), shown);
					await File.WriteAllTextAsync (Path.Combine (_session!.Context.EvidenceDirectory, check + ".sample-" + attempt.ToString (CultureInfo.InvariantCulture) + ".json"), JsonSerializer.Serialize (new
						{
							ObservedUtc = DateTimeOffset.UtcNow, _session.Context.RunId, _session.Context.PackageSha256,
							firstHub, lastHub, FirstProcessor = first.PropertyValues.Where (p => p.Key is "currentTemperature" or "targetTemperature" or "temperatureUnits" or "currentTemperatureLabel").ToDictionary (),
							LastProcessor = last.PropertyValues.Where (p => p.Key is "currentTemperature" or "targetTemperature" or "temperatureUnits" or "currentTemperatureLabel").ToDictionary (),
							Shown = shown, Stable = stable, Matched = matches, PhysicalCommandsSent = false, GaugePixelPositionVerified = false
							}), convergence.Token);
					if (matches) break;
					if (!ordinary) throw new InvalidDataException ("A valid ordinary hub temperature and heating target are required for this read-only comparison.");
					await Task.Delay (1000, convergence.Token);
					}
				}, timeout.Token);
			Assert.That (_navigation.HomeRestored, Is.True);
			await File.WriteAllTextAsync (Path.Combine (_session!.Context.EvidenceDirectory, check + ".result.json"), JsonSerializer.Serialize (new
				{
					Passed = true, HomeRestored = _navigation.HomeRestored, PhysicalCommandsSent = false,
					TemperatureUnits = units,
					Scope = "Displayed current temperature, label and heating target compared with stable independent hub readings in the original configured units; gauge presence only, not pixel position."
					}), timeout.Token);
			}
		}
	}