// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool AllowNativeThermostatControl
			{
			get; init;
			}
		}

	[TestCase (false), TestCase (true), Category ("LiveControl")]
	public async Task NativeThermostatInputsChangeHubAndRestorePolicy (bool boost)
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "Earlier restoration must be reconciled before more controls.");
		if (!_settings!.AllowNativeThermostatControl)
			Assert.Ignore ("Explicitly enable native thermostat temperature and boost controls.");
		if (_settings.ControlRooms.Length != 1 || string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One explicit room and absolute private hub settings are required.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret))
			throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		var control = _settings.ControlRooms.Single ();
		var binding = _settings.Rooms.Single (r => r.DeviceId == control.DeviceId);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (15));
		var original = await ReadRoomAsync (binding, timeout.Token);
		string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + (boost ? ".native-boost" : ".native-setpoint");
		await _navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, original.Name!, binding.PageTitle, async (_, token) =>
			{
				var session = new NativeTemperatureSession (this, hub, http, binding, control, original, check);
				var result = await RoomTemperatureCycle.RunAsync (session, boost, TimeSpan.FromMinutes (3), token);
				_roomStatePreserved = result.RestorationConfirmed;
				await session.RecordAsync ("result", result);
				Assert.That (result.Passed, Is.True, result.Detail);
			}, timeout.Token);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	private sealed class NativeTemperatureSession (GatewayUiTests fixture, HubSettings hub, HttpClient http,
		RoomBinding binding, ControlRoomBinding control, DeviceInfo original, string check) : IRoomTemperatureSession
		{
		private AndroidWorkflowSession Session => fixture._session!;
		private RoomTemperatureSnapshot? _original;
		private RoomTemperatureSnapshot? _intent;
		private RoomTemperatureRestorePlan? _plan;
		private int _inputs;
		private readonly HashSet<int> _restores = [];
		private AndroidHierarchy Page (AndroidHierarchy hierarchy) => CrestronHomeExtensionPages.RequirePage (hierarchy, [binding.PageTitle]);
		private static AndroidSelector Target => CrestronHomePages.Resource ("statusLabels_value") with { AncestorResourceId = CrestronHomePages.ResourcePrefix + "customdevice_thermostat_heatSetpoint" };
		private static RoomTemperatureActivity Activity (DeviceInfo room) => JsonSerializer.Deserialize<RoomTemperatureActivity> (room.PropertyValues["controlStatus"].GetString ()!)
			?? throw new InvalidDataException ("Room command activity is missing.");
		private string TemperatureUnits => original.PropertyValues["temperatureUnits"].GetString () switch
			{
			"Celsius" => "Celsius",
			"Fahrenheit" => "Fahrenheit",
			_ => throw new InvalidDataException ("Native controls require explicit Celsius or Fahrenheit units.")
			};
		private string Formatted (double value) => value.ToString ("0.0", CultureInfo.InvariantCulture) + (TemperatureUnits == "Celsius" ? "°C" : "°F");
		private bool DisplayMatches (AndroidHierarchy hierarchy, double target, bool boost)
			{
			var page = Page (hierarchy);
			var row = CrestronHomePages.ReadStatusAndButton (page, "Boost");
			return page.RequireUnique (Target).Text == Formatted (target) && row.Enabled &&
				string.Equals (row.Status, boost ? "Boost Active" : "Boost Off", StringComparison.OrdinalIgnoreCase) && row.Action == (boost ? "Boost Off" : "Boost On");
			}
		private async Task<JsonElement> ReadHub (string group, CancellationToken token)
			{
			using var response = await http.GetAsync ("http://" + hub.HubHost + "/data/v2/" + group + "/", token);
			response.EnsureSuccessStatusCode ();
			using var data = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
			return data.RootElement.Clone ();
			}
		public async Task<RoomTemperatureSnapshot> ReadAsync (CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			var before = await fixture.ReadRoomAsync (fixture._processor!, binding, token);
			var gateway = await fixture.ReadGatewayAsync (token);
			var domain = await ReadHub ("domain", token);
			var schedules = await ReadHub ("schedules", token);
			var physicalRoom = domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName);
			int roomId = physicalRoom.GetProperty ("id").GetInt32 ();
			string physical = hub.HubHost.Trim ().ToLowerInvariant ();
			var after = await fixture.ReadRoomAsync (fixture._processor!, binding, token);
			foreach (var room in new[] { before, after })
				if (room.Id != original.Id || room.Name != original.Name || room.ParentDeviceId != original.ParentDeviceId || room.LocationId != original.LocationId ||
					room.PropertyValues["controlDeviceId"].GetString () != physical + "/room/" + roomId.ToString (CultureInfo.InvariantCulture) ||
					room.PropertyValues["temperatureUnits"].GetString () != TemperatureUnits)
					throw new InvalidDataException ("Native controls require the unchanged, explicitly bound thermostat and temperature units.");
			var hierarchy = await Session.Device.CaptureAsync (token);
			double target = after.PropertyValues["targetTemperature"].GetDouble ();
			bool boost = after.PropertyValues["isBoostActive"].GetBoolean ();
			bool stable = Activity (before) == Activity (after) && before.PropertyValues["targetTemperature"].GetDouble () == target && before.PropertyValues["isBoostActive"].GetBoolean () == boost;
			var state = new RoomTemperatureSnapshot (new (physical, gateway.PropertyValues["driverLifetimeId"].GetString ()!,
				SuccessfulRefreshSequence.ParseTimestamp (gateway.PropertyValues["lastHubRefreshUtc"].GetString ()!), new (schedules, domain),
				gateway.PropertyValues["awayModeIsEnabled"].GetBoolean (), true), roomId, Activity (after), target, boost, stable && DisplayMatches (hierarchy, target, boost))
				{
				TemperatureUnits = TemperatureUnits
				};
			_original ??= state;
			_plan ??= RoomTemperatureRestoration.Capture (physicalRoom);
			return state;
			}
		public async Task RecordAsync (string phase, object value)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			var serialized = JsonSerializer.SerializeToElement (value);
			if (phase.StartsWith ("input-", StringComparison.Ordinal) && phase.EndsWith ("-intent", StringComparison.Ordinal))
				{
				fixture._roomStatePreserved = false;
				_intent = serialized.GetProperty ("Snapshot").Deserialize<RoomTemperatureSnapshot> ();
				}
			string directory = Path.Combine (Session.Context.EvidenceDirectory, check + ".records");
			Directory.CreateDirectory (directory);
			await using (var file = new FileStream (Path.Combine (directory, phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
				{
				await JsonSerializer.SerializeAsync (file, new
					{
					Session.Context.RunId,
					Session.Context.PackageSha256,
					Session.Context.ReleaseSourceCommit,
					Phase = phase,
					ObservedUtc = DateTimeOffset.UtcNow,
					Value = value
					});
				file.Flush (flushToDisk: true);
				}
			if (phase == "restored" || phase.StartsWith ("input-", StringComparison.Ordinal) && phase.EndsWith ("-observed", StringComparison.Ordinal))
				{
				var state = serialized.GetProperty ("Snapshot").Deserialize<RoomTemperatureSnapshot> ()!;
				using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (25));
				await Session.CaptureAsync (check + "." + phase, hierarchy =>
					{
						if (!DisplayMatches (hierarchy, state.HomeTarget, state.HomeBoost))
							throw new InvalidDataException ("UI state changed before evidence capture.");
					}, timeout.Token);
				}
			}
		public async Task InputAsync (RoomTemperatureAction action, CancellationToken token)
			{
			if (_original == null || _plan == null || _intent == null || _inputs >= 2)
				throw new InvalidOperationException ("No fresh recorded native control intent is available.");
			var current = await ReadAsync (token);
			RoomTemperatureRestoration.RequireGuarded (_plan, _original.Gateway, current.Gateway);
			if (current.Activity != _intent.Activity || current.HomeTarget != _intent.HomeTarget || current.HomeBoost != _intent.HomeBoost || !current.UiMatches)
				throw new InvalidDataException ("State changed after the control intent; input was not repeated.");
			bool boost = action is RoomTemperatureAction.BoostOn or RoomTemperatureAction.BoostOff;
			var selector = boost ? Text (action == RoomTemperatureAction.BoostOn ? "Boost On" : "Boost Off")
				: CrestronHomePages.Resource (action == RoomTemperatureAction.Raise ? "customdevice_thermostat_heatPlus" : "customdevice_thermostat_heatMinus");
			void Guard (AndroidHierarchy hierarchy)
				{
				var page = Page (hierarchy);
				if (!DisplayMatches (hierarchy, current.HomeTarget, current.HomeBoost) || !page.RequireUnique (selector).Enabled ||
					page.RequireUnique (selector) != hierarchy.RequireUnique (selector))
					throw new InvalidDataException ("Native input is not uniquely enabled on the observed thermostat page.");
				}
			await Session.CaptureAsync (check + ".before-input-" + (_inputs + 1), Guard, token);
			await RecordAsync ("tap-" + (++_inputs) + "-intent", new
				{
				Action = action
				});
			await Session.Device.TapAsync (selector, Guard, token);
			_intent = null;
			}
		public async Task RestoreAsync (int index, RoomTemperatureRestorePlan plan, JsonElement request, CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			if (_plan == null || _original == null || plan.RoomId != _plan.RoomId || index < 0 || index >= _plan.Requests.Length ||
				!JsonElement.DeepEquals (request, _plan.Requests[index]) || _restores.Contains (index))
				throw new InvalidDataException ("Compensation must match the captured plan and must not be repeated.");
			RoomTemperatureRestoration.RequireGuarded (_plan, _original.Gateway, (await ReadAsync (token)).Gateway);
			await RecordAsync ("restore-http-" + index + "-intent", new
				{
				plan.RoomId,
				Request = request
				});
			token.ThrowIfCancellationRequested ();
			_restores.Add (index);
			using var message = new HttpRequestMessage (HttpMethod.Patch, "http://" + hub.HubHost + "/data/v2/domain/Room/" + plan.RoomId.ToString (CultureInfo.InvariantCulture))
				{
				Content = new StringContent (request.GetRawText (), Encoding.UTF8, "application/json")
				};
			using var response = await http.SendAsync (message, token);
			await RecordAsync ("restore-http-" + index + "-response", new
				{
				Status = (int)response.StatusCode
				});
			response.EnsureSuccessStatusCode ();
			}
		}
	}