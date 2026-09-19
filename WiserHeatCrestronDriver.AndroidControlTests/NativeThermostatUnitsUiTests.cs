// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;
using NUnit.Framework;
using WiserHeatCrestronDriver.ConfigurationProbe;
using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool AllowNativeThermostatUnitConfiguration { get; init; }
		}

	[TestCase ("setpoint"), TestCase ("boost"), TestCase ("off"), TestCase ("minimum"), TestCase ("maximum"), Category ("LiveConfiguration")]
	public async Task NativeThermostatAlternateUnitsRestoreConfigurationAndPolicy (string exercise)
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "Earlier restoration must be reconciled.");
		if (!_settings!.AllowNativeThermostatUnitConfiguration)
			Assert.Ignore ("Enable the separate native thermostat unit-configuration scope.");
		bool boundary = exercise is "minimum" or "maximum";
		if (boundary ? !_settings.AllowNativeThermostatBoundaryControl : exercise == "off" ? !_settings.AllowNativeThermostatOffControl : !_settings.AllowNativeThermostatControl)
			Assert.Ignore ("Enable the selected physical-control scope as well as unit configuration.");
		if (_settings.ControlRooms.Length != 1 || string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One explicit physical room and absolute private hub settings are required.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret)) throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		var binding = _settings.Rooms.Single (r => r.DeviceId == _settings.ControlRooms.Single ().DeviceId);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (75));
		var session = new NativeUnitsSession (this, hub, http, binding, exercise);
		var before = await session.ReadAsync (timeout.Token);
		string units = before.Configuration.Items.Single (i => i.Id == "TemperatureUnits").CurrentValue!.Value.GetString ()!;
		bool restored = false;
		try
			{
			var result = await TemperatureUnitsCycle.RunAsync (session, units == "Celsius" ? "Fahrenheit" : "Celsius", TimeSpan.FromMinutes (30), timeout.Token);
			restored = result.RestorationConfirmed;
			await session.RecordAsync ("units-result", result);
			Assert.That (result.Passed, Is.True, result.Detail);
			var originalRoom = await ReadRoomAsync (binding, timeout.Token);
			await _navigation!.InspectRoomExtensionPagesAsync ("wiser.units-" + exercise + ".restored", binding.RoomName, originalRoom.Name!, binding.PageTitle, async (_, token) =>
				{
				var native = new NativeTemperatureSession (this, hub, http, binding, _settings.ControlRooms.Single (), originalRoom, "wiser.units-" + exercise + ".restored");
				var snapshot = await native.ReadAsync (token);
				int raw = RoomTemperatureRestoration.Room (snapshot.Gateway.Hub, snapshot.RoomId).GetProperty ("CurrentSetPoint").GetInt32 ();
				double expected = raw == -200 ? -20 : units == "Fahrenheit" ? raw * 0.18d + 32d : raw / 10d;
				Assert.That (snapshot.TemperatureUnits, Is.EqualTo (units));
				Assert.That (snapshot.HomeTarget, Is.EqualTo (expected).Within (0.001));
				Assert.That (snapshot.UiMatches, Is.True, "Original-unit rendered feedback must agree after configuration restoration.");
				await session.RecordAsync ("units-restored-ui", new { Snapshot = snapshot });
				}, timeout.Token);
			Assert.That (_navigation.HomeRestored, Is.True);
			}
		finally
			{
			// A nested physical cycle can restore the room while configuration still needs reconciliation.
			_roomStatePreserved &= restored;
			}
		}

	private sealed class NativeUnitsSession (GatewayUiTests fixture, HubSettings hub, HttpClient http, RoomBinding binding, string exercise) : ITemperatureUnitsSession
		{
		private const string COMMAND = "cp.driverConfiguration:applyConfiguration";
		private TemperatureUnitsObservation? _last;
		private string? _instanceIdentity;
		private GatewayAwaySnapshot? _physicalOriginal;
		private GatewayAwaySnapshot? _physicalLast;
		private AndroidWorkflowSession Session => fixture._session!;

		public async Task<TemperatureUnitsObservation> ReadAsync (CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			var device = await fixture._processor!.GetDeviceAsync (Session.Context.InstalledDriverId, token) ?? throw new InvalidDataException ("Gateway disappeared.");
			var config = await DriverConfigurationInspection.GetAsync (fixture._processor, device.Id, token);
			_ = ConfigurationCompatibility.Identity (config);
			var values = config.Items.ToDictionary (i => i.Id, i => i.CurrentValue!.Value.GetString ()!);
			var room = await fixture._processor.GetDeviceAsync (binding.DeviceId, token) ?? throw new InvalidDataException ("Bound thermostat disappeared.");
			if (device.ParentDeviceId != -6 || config.DeviceId != device.Id || config.Name != device.Name || config.Model != device.Model ||
				config.Version != Session.Context.DriverVersion || device.PropertyValues["cp.driverInformation:version"].GetString () != Session.Context.DriverVersion ||
				HashPeerBinding (values["_Host_"], values["HubSecret"]) != HashPeerBinding (hub.HubHost, hub.Secret) || !device.Commands.Contains (COMMAND) ||
				room.ParentDeviceId != device.Id || room.Model != "Room Thermostat")
				throw new InvalidDataException ("Gateway, room or independent hub binding differs from the reserved candidate.");
			string identity = JsonSerializer.Serialize (new { device.Id, device.Name, device.Model, device.LocationId,
				Lifetime = device.PropertyValues["driverLifetimeId"].GetString (), RoomId = room.Id, RoomName = room.Name, RoomLocation = room.LocationId });
			_instanceIdentity ??= identity;
			if (identity != _instanceIdentity) throw new InvalidDataException ("Gateway or thermostat identity changed during the unit cycle.");
			async Task<JsonElement> Get (string group)
				{
				using var response = await http.GetAsync ("http://" + hub.HubHost + "/data/v2/" + group + "/", token);
				response.EnsureSuccessStatusCode ();
				using var data = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
				return data.RootElement.Clone ();
				}
			var domain = await Get ("domain");
			var schedules = await Get ("schedules");
			var physical = new GatewayAwaySnapshot (HashPeerBinding (hub.HubHost, hub.Secret), device.PropertyValues["driverLifetimeId"].GetString ()!,
				SuccessfulRefreshSequence.ParseTimestamp (device.PropertyValues["lastHubRefreshUtc"].GetString ()!), new (schedules, domain),
				device.PropertyValues["awayModeIsEnabled"].GetBoolean (), true);
			_physicalOriginal ??= physical;
			_physicalLast = physical;
			GatewayAwayCycle.RequirePreserved (_physicalOriginal, physical);
			if (GatewayAwayCycle.IsAway (_physicalOriginal) != GatewayAwayCycle.IsAway (physical)) throw new InvalidDataException ("Away policy changed during the unit exercise.");
			bool ready = device.PropertyValues["readyIndicator:isReady"].GetBoolean () && device.PropertyValues["onlineIndicator:isOnline"].GetBoolean () &&
				device.PropertyValues["cp.driverConfiguration:driverLoadingStatus"].GetString () == "Loaded" &&
				room.PropertyValues["temperatureUnits"].GetString () == values["TemperatureUnits"];
			return _last = new (config, ready);
			}

		public async Task ApplyAsync (string units, CancellationToken token)
			{
			if (units is not ("Celsius" or "Fahrenheit")) throw new InvalidDataException ("Unsupported units.");
			var previous = _last ?? throw new InvalidDataException ("Observe configuration before applying units.");
			var current = await ReadAsync (token);
			if (!current.Ready || ConfigurationCompatibility.Identity (current.Configuration) != ConfigurationCompatibility.Identity (previous.Configuration))
				throw new InvalidDataException ("Configuration changed before input; no request was repeated.");
			var reply = await fixture._processor!.ExecuteDeviceCommandAsync (Session.Context.InstalledDriverId, COMMAND, new
				{
				configurationItemValues = new Dictionary<string, string> { ["TemperatureUnits"] = units }, isoCulture = "en-GB"
				}, token);
			if (reply is { ValueKind: not JsonValueKind.Null } && (reply.Value.ValueKind != JsonValueKind.Array || reply.Value.GetArrayLength () != 0))
				throw new InvalidDataException ("Configuration was not accepted; no request was repeated.");
			}

		public async Task<TemperatureUnitsExercise> ExerciseAsync (CancellationToken token)
			{
			try
				{
				await fixture.RunNativeControlAsync (exercise == "boost", exercise == "off", exercise is "minimum" or "maximum" ? exercise == "maximum" : null, token, alternateUnits: true);
				return new (true, fixture._roomStatePreserved);
				}
			catch (Exception failure)
				{
				await RecordAsync ("units-physical-exercise-failure", new { ExceptionType = failure.GetType ().FullName, PhysicalRestorationConfirmed = fixture._roomStatePreserved });
				return new (false, fixture._roomStatePreserved);
				}
			}

		public async Task RecordAsync (string phase, object value)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			string directory = Path.Combine (Session.Context.EvidenceDirectory, "wiser.native-units-" + exercise + ".records");
			Directory.CreateDirectory (directory);
			await using var file = new FileStream (Path.Combine (directory, phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			await JsonSerializer.SerializeAsync (file, new { Session.Context.RunId, Session.Context.PackageSha256, Phase = phase, ObservedUtc = DateTimeOffset.UtcNow,
				InstanceIdentity = _instanceIdentity, PhysicalState = _physicalLast, Value = value });
			file.Flush (true);
			}
		}
	}