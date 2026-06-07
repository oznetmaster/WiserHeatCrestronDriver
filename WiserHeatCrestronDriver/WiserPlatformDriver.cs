using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.EntityModel.Logging;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

using WiserHeatApiV2;

namespace WiserHeat.CrestronDriver;

public sealed class WiserPlatformDriver : ReflectedAttributeDriverEntity
	{
	private readonly DriverControllerCreationArgs _args;
	private readonly DriverImplementationResources _resources;
	private readonly DriverControllerLogger _logger;
	private readonly string _driverLogId;
	private readonly WiserWorkQueue _workQueue = new ();
	private readonly Dictionary<string, WiserRoomEntity> _roomEntities = new (StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ConfigurableDriverEntity> _roomControllers = new (StringComparer.OrdinalIgnoreCase);
	private readonly object _entitiesLock = new ();

	private WiserAPI _api;
	private string _hubIpAddress = string.Empty;
	private string _hubSecret = string.Empty;
	private string _temperatureUnits = "Celsius";
	private double _boostDelta = 2.0;
	private int _boostDurationMinutes = 60;

	internal DataDrivenConfigurationController ConfigurationController
		{
		get;
		}

	internal string TemperatureUnits => _temperatureUnits;
	internal double BoostDelta => _boostDelta;
	internal int BoostDurationMinutes => _boostDurationMinutes;
	internal bool HasHeatingSchedules => _api?.Schedules?.HeatingSchedules?.Count > 0;

	[EntityProperty (Id = "platformStatus", FriendlyName = "Platform Status")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string PlatformStatus
		{
		get; private set;
		} = "Not configured";

	[EntityProperty (Id = "platformLastError", FriendlyName = "Platform Last Error")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string PlatformLastError
		{
		get; private set;
		} = string.Empty;

	[EntityProperty (
		Id = "platform:managedDevices",
		Type = DriverEntityValueType.DeviceDictionary,
		ItemTypeRef = "platform:ManagedDevice",
		FriendlyName = "Managed Rooms")]
	public IDictionary<string, PlatformManagedDevice> ManagedDevices
		{
		get; private set;
		}

	[EntityProperty (Id = "onlineIndicator:isOnline")]
	public bool OnlineIndicatorIsOnline
		{
		get; private set;
		}

	[EntityProperty (Id = "readyIndicator:isReady")]
	public bool ReadyIndicatorIsReady
		{
		get; private set;
		}

	public WiserPlatformDriver (
		DriverControllerCreationArgs args,
		DriverImplementationResources resources)
		: base (DriverController.RootControllerId)
		{
		_args = args;
		_resources = resources;
		_logger = args.Logger;
		_driverLogId = args.DriverId;
		ManagedDevices = new Dictionary<string, PlatformManagedDevice> ();

		var cfgArgs = DataDrivenConfigurationControllerArgs.FromResources (args, resources, ControllerId);
		ConfigurationController = new DelegateDataDrivenConfigurationController (
			cfgArgs,
			ApplyConfigurationItems,
			null,
			null);
		}

	public override void Dispose ()
		{
		_workQueue.Stop ();
		_api?.Dispose ();
		base.Dispose ();
		}

	private void Log (string message) =>
		_logger?.Log (_driverLogId, LogEntryLevel.Info, message);

	private void LogError (string message) =>
		_logger?.Log (_driverLogId, LogEntryLevel.Error, message);

	private void UpdateStatus (string status, string error = null)
		{
		PlatformStatus = status ?? string.Empty;
		if (error != null)
			PlatformLastError = error;

		NotifyPropertyChanged ("platformStatus", new DriverEntityValue (PlatformStatus));
		NotifyPropertyChanged ("platformLastError", new DriverEntityValue (PlatformLastError));
		}

	private void SetOnline (bool online)
		{
		if (OnlineIndicatorIsOnline == online)
			return;

		OnlineIndicatorIsOnline = online;
		NotifyPropertyChanged ("onlineIndicator:isOnline", new DriverEntityValue (online));
		}

	private void SetReady (bool ready)
		{
		if (ReadyIndicatorIsReady == ready)
			return;

		ReadyIndicatorIsReady = ready;
		NotifyPropertyChanged ("readyIndicator:isReady", new DriverEntityValue (ready));
		}

	private ConfigurationItemErrors ApplyConfigurationItems (
		DataDrivenConfigurationController.ApplyConfigurationAction action,
		string stepId,
		IDictionary<string, DriverEntityValue?> values)
		{
		void ApplyValues ()
			{
			if (values.TryGetValue ("_Host_", out DriverEntityValue? hubIp) && hubIp.HasValue)
				_hubIpAddress = hubIp.Value.GetValue<string> () ?? _hubIpAddress;

			if (values.TryGetValue ("HubSecret", out DriverEntityValue? hubSecret) && hubSecret.HasValue)
				_hubSecret = hubSecret.Value.GetValue<string> () ?? _hubSecret;

			if (values.TryGetValue ("TemperatureUnits", out DriverEntityValue? units) && units.HasValue)
				_temperatureUnits = units.Value.GetValue<string> () ?? _temperatureUnits;

			if (values.TryGetValue ("BoostDelta", out DriverEntityValue? boostDelta) && boostDelta.HasValue)
				_boostDelta = ReadDoubleValue (boostDelta.Value, _boostDelta);

			if (values.TryGetValue ("BoostDurationMinutes", out DriverEntityValue? boostMinutes) && boostMinutes.HasValue)
				_boostDurationMinutes = ReadIntValue (boostMinutes.Value, _boostDurationMinutes);
			}

		ConfigurationItemErrors ValidateConnectionItems ()
			{
			if (!string.IsNullOrWhiteSpace (_hubIpAddress) && !string.IsNullOrWhiteSpace (_hubSecret))
				return null;

			SetReady (false);
			SetOnline (false);
			UpdateStatus ("Configuration incomplete", "Hub IP Address and Hub Secret are required.");
			return new ConfigurationItemErrors (
				new Dictionary<string, string>
					{
					["_Host_"] = string.IsNullOrWhiteSpace (_hubIpAddress) ? "Hub IP Address is required." : string.Empty,
					["HubSecret"] = string.IsNullOrWhiteSpace (_hubSecret) ? "Hub Secret is required." : string.Empty,
					}.Where (kv => !string.IsNullOrWhiteSpace (kv.Value)).ToDictionary (kv => kv.Key, kv => kv.Value),
				"Wiser hub address and secret are required.");
			}

		switch (action)
			{
			case DataDrivenConfigurationController.ApplyConfigurationAction.ApplyAll:
				Log ("ApplyConfigurationItems: action=ApplyAll stepId=" + (stepId ?? "(none)"));
				ApplyValues ();
				ConfigurationItemErrors allErrors = ValidateConnectionItems ();
				if (allErrors != null)
					return allErrors;

				_ = ConnectAndDiscoverAsync ();
				return null;

			case DataDrivenConfigurationController.ApplyConfigurationAction.ApplyStep:
				Log ("ApplyConfigurationItems: action=ApplyStep stepId=" + (stepId ?? "(none)"));
				ApplyValues ();

				if (string.Equals (stepId, "Connection", StringComparison.OrdinalIgnoreCase))
					return ValidateConnectionItems ();

				if (string.Equals (stepId, "HeatSettings", StringComparison.OrdinalIgnoreCase))
					return null;

				return null;

			case DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues:
				Log ("ApplyConfigurationItems: action=ClearValues");
				_api?.Dispose ();
				_api = null;
				_workQueue.SetClient (null);
				SetReady (false);
				SetOnline (false);
				UpdateStatus ("Configuration cleared", string.Empty);
				return null;

			default:
				return null;
			}
		}

	private static double ReadDoubleValue (DriverEntityValue value, double fallback)
		{
		try
			{
			return value.GetValue<double> ();
			}
		catch
			{
			try
				{
				return value.GetValue<int> ();
				}
			catch
				{
				try
					{
					string text = value.GetValue<string> ();
					return double.TryParse (text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
						? parsed
						: fallback;
					}
				catch
					{
					return fallback;
					}
				}
			}
		}

	private static int ReadIntValue (DriverEntityValue value, int fallback)
		{
		try
			{
			return value.GetValue<int> ();
			}
		catch
			{
			try
				{
				return (int)Math.Round (value.GetValue<double> (), MidpointRounding.AwayFromZero);
				}
			catch
				{
				try
					{
					string text = value.GetValue<string> ();
					return int.TryParse (text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
						? parsed
						: fallback;
					}
				catch
					{
					return fallback;
					}
				}
			}
		}

	private async Task ConnectAndDiscoverAsync ()
		{
		try
			{
			Log (
				"Connect task started; host=" + _hubIpAddress +
				" units=" + _temperatureUnits +
				" boostDelta=" + _boostDelta.ToString (CultureInfo.InvariantCulture) +
				" boostDurationMinutes=" + _boostDurationMinutes);
			UpdateStatus ("Connecting", string.Empty);
			SetReady (true);
			WiserUnits units = string.Equals (_temperatureUnits, "Fahrenheit", StringComparison.OrdinalIgnoreCase)
				? WiserUnits.Imperial
				: WiserUnits.Metric;

			_api?.Dispose ();
			_api = new WiserAPI (_hubIpAddress, _hubSecret, units);
			Log ("Wiser API client created; initializing against host=" + _hubIpAddress);
			await _api.InitializeAsync (CancellationToken.None).ConfigureAwait (false);
			Log ("Connected to Wiser API");
			_workQueue.SetClient (_api);
			SetOnline (true);
			UpdateStatus ("Connected", string.Empty);
			await RefreshRoomsAsync ().ConfigureAwait (false);
			}
		catch (Exception ex)
			{
			LogError ("Wiser connection failed: " + ex);
			SetOnline (false);
			SetReady (false);
			UpdateStatus ("Offline", ex.Message);
			}
		}

	internal async Task RefreshRoomsAsync ()
		{
		if (_api == null)
			{
			Log ("RefreshRoomsAsync skipped: API client is null");
			return;
			}

		Log ("RefreshRoomsAsync: reading hub data");
		await _api.ReadHubDataAsync (CancellationToken.None).ConfigureAwait (false);
		List<WiserRoom> rooms = _api.Rooms?.All ?? [];
		Log ("Discovered " + rooms.Count + " total rooms");

		var managed = new Dictionary<string, PlatformManagedDevice> (StringComparer.OrdinalIgnoreCase);
		var controllersToAdd = new List<ConfigurableDriverEntity> ();
		lock (_entitiesLock)
			{
			foreach (WiserRoom room in rooms)
				{
				Log (
					"Evaluating room: " + (room.Name ?? "(unnamed)") +
					" | Id=" + room.Id +
					" | CurrentTemp=" + room.CurrentTemperature.ToString (CultureInfo.InvariantCulture) +
					" | TargetTemp=" + room.CurrentTargetTemperature.ToString (CultureInfo.InvariantCulture) +
					" | Mode=" + (room.Mode ?? "(none)"));
				string controllerId = $"room_{room.Id}";
				if (!_roomEntities.TryGetValue (controllerId, out WiserRoomEntity entity))
					{
					entity = new WiserRoomEntity (
						controllerId,
						room,
						this,
						_logger,
						_resources,
						_args.DriverDataDirectoryPath,
						_driverLogId);
					_roomEntities[controllerId] = entity;
					var controller = new ConfigurableDriverEntity (controllerId, entity, null);
					_roomControllers[controllerId] = controller;
					controllersToAdd.Add (controller);
					Log ("Queued room: " + room.Name + " (id=" + controllerId + ")");
					}
				else
					{
					entity.UpdateFromRoom (room, _temperatureUnits);
					Log ("Updated room: " + room.Name + " (id=" + controllerId + ")");
					}

				managed[controllerId] = new PlatformManagedDevice (
					DeviceUxCategory.Thermostat,
					room.Name ?? $"Room {room.Id}",
					"Drayton Wiser",
					"Room Thermostat",
					room.Id.ToString ());
				}
			}

		if (controllersToAdd.Count > 0)
			{
			Log ("RefreshRoomsAsync - UpdateSubControllers start count=" + controllersToAdd.Count);
			UpdateSubControllers (controllersToAdd, null);
			Log ("RefreshRoomsAsync - UpdateSubControllers complete count=" + controllersToAdd.Count);
			lock (_entitiesLock)
				{
				foreach (ConfigurableDriverEntity controller in controllersToAdd)
					{
					if (_roomEntities.TryGetValue (controller.ControllerId, out WiserRoomEntity roomEntity))
						roomEntity.StartPolling ();
					}
				}
			}

		ManagedDevices = managed;
		Log ("RefreshRoomsAsync - publishing platform:managedDevices count=" + ManagedDevices.Count);
		NotifyPropertyChanged ("platform:managedDevices", CreateValueForEntries (ManagedDevices));

		if (ManagedDevices.Count == 0)
			UpdateStatus ("Connected - no rooms discovered", string.Empty);
		else
			UpdateStatus ("Connected - rooms discovered: " + ManagedDevices.Count, string.Empty);

		Log ("Discovery complete");
		}

	internal async Task<bool> AdjustRoomSetpointAsync (int roomId, double delta)
		{
		if (_api == null)
			return false;

		WiserRoom room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		double newSetpoint = Math.Round (room.CurrentTargetTemperature + delta, 1, MidpointRounding.AwayFromZero);
		await room.SetTargetTemperatureAsync (newSetpoint, CancellationToken.None).ConfigureAwait (false);
		await RefreshRoomsAsync ().ConfigureAwait (false);
		return true;
		}

	internal async Task<bool> TriggerRoomBoostAsync (int roomId)
		{
		if (_api == null)
			return false;

		WiserRoom room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		if (room.IsBoost)
			await room.CancelBoostAsync (CancellationToken.None).ConfigureAwait (false);
		else
			await room.BoostAsync (_boostDelta, _boostDurationMinutes, CancellationToken.None).ConfigureAwait (false);

		await RefreshRoomsAsync ().ConfigureAwait (false);
		return true;
		}

	internal async Task<bool> AdvanceRoomScheduleAsync (int roomId)
		{
		if (_api == null)
			return false;

		WiserRoom room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		await room.ScheduleAdvanceAsync (CancellationToken.None).ConfigureAwait (false);
		await RefreshRoomsAsync ().ConfigureAwait (false);
		return true;
		}

	internal async Task<bool> CycleRoomAssignedScheduleAsync (int roomId, int direction)
		{
		if (_api == null)
			return false;

		WiserRoom room = _api.Rooms?.GetById (roomId);
		List<WiserHeatingSchedule> schedules = _api.Schedules?.HeatingSchedules?
			.OrderBy (schedule => schedule.Name ?? string.Empty)
			.ThenBy (schedule => schedule.Id)
			.ToList () ?? [];

		if (room == null || schedules.Count == 0)
			return false;

		int currentIndex = schedules.FindIndex (schedule => schedule.Id == room.ScheduleId || schedule.Id == room.Schedule?.Id);
		int nextIndex;
		if (currentIndex < 0)
			nextIndex = direction < 0 ? schedules.Count - 1 : 0;
		else
			nextIndex = (currentIndex + (direction < 0 ? -1 : 1) + schedules.Count) % schedules.Count;

		WiserHeatingSchedule targetSchedule = schedules[nextIndex];
		await targetSchedule.AssignScheduleAsync ([roomId], true, CancellationToken.None).ConfigureAwait (false);
		await RefreshRoomsAsync ().ConfigureAwait (false);
		return true;
		}

	internal async Task<bool> SetRoomScheduleEnabledAsync (int roomId, bool enabled)
		{
		if (_api == null)
			return false;

		WiserRoom room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		if (enabled)
			{
			if (room.Schedule == null)
				{
				List<WiserHeatingSchedule> schedules = _api.Schedules?.HeatingSchedules?
					.OrderBy (schedule => schedule.Name ?? string.Empty)
					.ThenBy (schedule => schedule.Id)
					.ToList () ?? [];

				if (schedules.Count == 0)
					return false;

				await schedules[0].AssignScheduleAsync ([roomId], true, CancellationToken.None).ConfigureAwait (false);
				}

			string autoMode = FindPreferredRoomMode (room, "Auto", "Scheduled", "Schedule");
			if (!string.IsNullOrWhiteSpace (autoMode))
				await room.SetModeAsync (autoMode, CancellationToken.None).ConfigureAwait (false);
			}
		else
			{
			string manualMode = FindPreferredRoomMode (room, "Manual", "Fixed");
			if (!string.IsNullOrWhiteSpace (manualMode))
				await room.SetModeAsync (manualMode, CancellationToken.None).ConfigureAwait (false);
			else
				await room.SetManualTemperatureAsync (room.CurrentTargetTemperature, CancellationToken.None).ConfigureAwait (false);
			}

		await RefreshRoomsAsync ().ConfigureAwait (false);
		return true;
		}

	private static string FindPreferredRoomMode (WiserRoom room, params string[] preferredModes)
		{
		List<string> availableModes = WiserRoom.AvailableModes;
		if (availableModes == null || availableModes.Count == 0)
			return preferredModes.FirstOrDefault (mode => !string.IsNullOrWhiteSpace (mode));

		foreach (string preferredMode in preferredModes)
			{
			string match = availableModes.FirstOrDefault (mode => string.Equals (mode, preferredMode, StringComparison.OrdinalIgnoreCase));
			if (!string.IsNullOrWhiteSpace (match))
				return match;
			}

		foreach (string preferredMode in preferredModes)
			{
			string match = availableModes.FirstOrDefault (mode =>
				!string.IsNullOrWhiteSpace (mode) &&
				mode.IndexOf (preferredMode, StringComparison.OrdinalIgnoreCase) >= 0);
			if (!string.IsNullOrWhiteSpace (match))
				return match;
			}

		return null;
		}
	}
