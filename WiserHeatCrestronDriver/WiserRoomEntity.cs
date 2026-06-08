using System;
using System.IO;
using System.Collections.Generic;
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

internal sealed class WiserRoomEntity : ReflectedAttributeDriverEntity
	{
	private int _frameworkReady;
	private readonly DriverControllerLogger _logger;
	private readonly string _driverLogId;
	private readonly WiserPlatformDriver _platform;
	private readonly UiDefinitionProperty _uiDefinition;
	private readonly Dictionary<string, DriverEntityValue> _propertyCache = [];
	private DriverEntityAvailableValue[] _scheduleValues = [];
	private WiserRoom _room;

	public WiserRoomEntity (
		string controllerId,
		WiserRoom room,
		WiserPlatformDriver platform,
		DriverControllerLogger logger,
		DriverImplementationResources resources,
		string driverDataDirectoryPath,
		string driverLogId)
		: base (controllerId)
		{
		_logger = logger;
		_driverLogId = driverLogId;
		_platform = platform;
		_room = room;
		DeviceLabel = room?.Name ?? controllerId;
		CurrentTemperature = room?.CurrentTemperature ?? 0;
		TargetTemperature = room?.CurrentTargetTemperature ?? 0;
		DisplayedSetpoint = room?.DisplayedSetpoint ?? TargetTemperature;
		IsBoostActive = IsRoomBoostActive (room);
		BoostStateLabel = IsBoostActive ? "^BoostOnLabel" : "^BoostOffLabel";
		BoostActionLabel = IsBoostActive ? "^BoostOffActionLabel" : "^BoostOnActionLabel";
		TemperatureUnits = "Celsius";
		IsScheduleAvailable = platform?.HasHeatingSchedules == true;
		ScheduleSummary = BuildScheduleSummary (room);
		ScheduleEnabled = IsScheduleEnabled (room);
		CanEnableSchedule = !ScheduleEnabled && IsScheduleAvailable;
		CanDisableSchedule = ScheduleEnabled;
		ScheduleStatusLabel = BuildScheduleStatusLabel (room);
		ScheduleToggleActionLabel = BuildScheduleToggleActionLabel (room);
		SelectedScheduleName = BuildSelectedScheduleName (room);
		SelectedScheduleId = BuildSelectedScheduleId (room);
		ScheduleSelectorEnabled = platform?.HasHeatingSchedules == true;
		CurrentTemperatureLabel = $"{CurrentTemperature:0.0}°";
		TileIcon = BuildTileIcon (room);
		OnlineIndicatorIsOnline = true;
		ReadyIndicatorIsReady = true;
		RefreshPropertyCache ();

		try
			{
			var baseDir = driverDataDirectoryPath ?? Path.GetTempPath ();
			var roomRoot = Path.Combine (baseDir, "room");
			_uiDefinition = UiDefinitionProperty.LoadFromDirectoryIfExists (roomRoot, resources.InitLogger, LogEntryLevel.Error);
			}
		catch (Exception ex)
			{
			_logger?.Log (_driverLogId, LogEntryLevel.Error, "UiDefinition load failed: " + ex.Message);
			}

		AddProperty (this, UiDefinitionProperty.Name, _uiDefinition);

		var doCommand = new ExtensionDoCommandExecutor (GetCommand, resources.Logger);
		AddCommand (this, ExtensionDoCommandExecutor.CommandName, doCommand);

		var setPropertyValue = new ExtensionSetPropertyValueExecutor (GetCommand, resources.Logger);
		AddCommand (this, ExtensionSetPropertyValueExecutor.CommandName, setPropertyValue);

		RegisterExtensionSurface ();
		}

	public DeviceUxCategory UxCategory => DeviceUxCategory.Thermostat;

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

	[EntityProperty (Id = "deviceLabel", FriendlyName = "Room Label")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string DeviceLabel
		{
		get; private set;
		}

	[EntityProperty (Id = "currentTemperature", FriendlyName = "Current Temperature")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double CurrentTemperature
		{
		get; private set;
		}

	[EntityProperty (Id = "targetTemperature", FriendlyName = "Target Temperature")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double TargetTemperature
		{
		get; private set;
		}

	[EntityProperty (Id = "displayedSetpoint", FriendlyName = "Displayed Setpoint")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double DisplayedSetpoint
		{
		get; private set;
		}

	[EntityProperty (Id = "isBoostActive", FriendlyName = "Boost Active")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool IsBoostActive
		{
		get; private set;
		}

	[EntityProperty (Id = "boostStateLabel", FriendlyName = "Boost State Label")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string BoostStateLabel
		{
		get; private set;
		}

	[EntityProperty (Id = "boostActionLabel", FriendlyName = "Boost Action Label")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string BoostActionLabel
		{
		get; private set;
		}

	[EntityProperty (Id = "temperatureUnits", FriendlyName = "Temperature Units")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string TemperatureUnits
		{
		get; private set;
		}

	[EntityProperty (Id = "isScheduleAvailable", FriendlyName = "Schedule Available")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool IsScheduleAvailable
		{
		get; private set;
		}

	[EntityProperty (Id = "scheduleSummary", FriendlyName = "Schedule Summary")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string ScheduleSummary
		{
		get; private set;
		}

	[EntityProperty (Id = "scheduleEnabled", FriendlyName = "Schedule Enabled")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool ScheduleEnabled
		{
		get; private set;
		}

	[EntityProperty (Id = "canEnableSchedule", FriendlyName = "Can Enable Schedule")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool CanEnableSchedule
		{
		get; private set;
		}

	[EntityProperty (Id = "canDisableSchedule", FriendlyName = "Can Disable Schedule")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool CanDisableSchedule
		{
		get; private set;
		}

	[EntityProperty (Id = "scheduleStatusLabel", FriendlyName = "Schedule Status")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string ScheduleStatusLabel
		{
		get; private set;
		}

	[EntityProperty (Id = "scheduleToggleActionLabel", FriendlyName = "Schedule Toggle Action Label")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string ScheduleToggleActionLabel
		{
		get; private set;
		}

	[EntityProperty (Id = "selectedScheduleName", FriendlyName = "Selected Schedule")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string SelectedScheduleName
		{
		get; private set;
		}

	public string SelectedScheduleId
		{
		get; private set;
		}

	private DriverEntityAvailableValue[] ScheduleValues
		{
		get => _scheduleValues;
		set => _scheduleValues = value ?? [];
		}

	[EntityProperty (Id = "scheduleSelectorEnabled", FriendlyName = "Schedule Selector Enabled")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool ScheduleSelectorEnabled
		{
		get; private set;
		}

	[EntityProperty (Id = "currentTemperatureLabel", FriendlyName = "Current Temperature Label")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string CurrentTemperatureLabel
		{
		get; private set;
		}

	[EntityProperty (Id = "tileIcon", FriendlyName = "Tile Icon")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string TileIcon
		{
		get; private set;
		}


	[EntityCommand (Id = "increaseSetpoint", FriendlyName = "Increase Setpoint")]
	[EntityCommandMetadata (Programmable = true)]
	public void IncreaseSetpoint ()
		{
		if (_room == null)
			return;

		_ = FireAndForgetAsync (
			() => _platform.AdjustRoomSetpointAsync (_room.Id, 0.5),
			"increase setpoint");
		}

	[EntityCommand (Id = "decreaseSetpoint", FriendlyName = "Decrease Setpoint")]
	[EntityCommandMetadata (Programmable = true)]
	public void DecreaseSetpoint ()
		{
		if (_room == null)
			return;

		_ = FireAndForgetAsync (
			() => _platform.AdjustRoomSetpointAsync (_room.Id, -0.5),
			"decrease setpoint");
		}

	[EntityCommand (Id = "boost", FriendlyName = "Boost")]
	[EntityCommandMetadata (Programmable = true)]
	public void Boost ()
		{
		if (_room == null)
			return;

		_ = FireAndForgetAsync (
			() => _platform.TriggerRoomBoostAsync (_room.Id),
			"boost");
		}

	[EntityCommand (Id = "openSchedule", FriendlyName = "Open Schedule")]
	[EntityCommandMetadata (Programmable = true)]
	public void OpenSchedule ()
		{
		LogInfo ($"OpenSchedule invoked; frameworkReady={_frameworkReady}, selectorEnabled={ScheduleSelectorEnabled}, selectedScheduleId='{SelectedScheduleId ?? string.Empty}'");
		RefreshScheduleValues (notify: _frameworkReady != 0);
		}

	[EntityCommand (Id = "enableSchedule", FriendlyName = "Enable Schedule")]
	[EntityCommandMetadata (Programmable = true)]
	public void EnableSchedule ()
		{
		if (_room == null)
			return;

		_ = FireAndForgetAsync (
			() => _platform.SetRoomScheduleEnabledAsync (_room.Id, true),
			"enable schedule");
		}

	[EntityCommand (Id = "disableSchedule", FriendlyName = "Disable Schedule")]
	[EntityCommandMetadata (Programmable = true)]
	public void DisableSchedule ()
		{
		if (_room == null)
			return;

		_ = FireAndForgetAsync (
			() => _platform.SetRoomScheduleEnabledAsync (_room.Id, false),
			"disable schedule");
		}

	public void UpdateFromRoom (WiserRoom room, string temperatureUnits)
		{
		_room = room ?? _room;
		if (_room == null)
			return;

		WiserHeatingSchedule assignedSchedule = ResolveAssignedSchedule (_room);
		LogInfo ($"UpdateFromRoom start; roomId={_room.Id}, mode='{_room.Mode ?? string.Empty}', scheduleId={assignedSchedule?.Id ?? _room.ScheduleId}, scheduleName='{ResolveScheduleNameForLog (_room, assignedSchedule)}', temperatureUnits='{temperatureUnits ?? string.Empty}'");

		DeviceLabel = _room.Name ?? DeviceLabel;
		CurrentTemperature = _room.CurrentTemperature;
		TargetTemperature = _room.CurrentTargetTemperature;
		DisplayedSetpoint = _room.DisplayedSetpoint;
		IsBoostActive = IsRoomBoostActive (_room);
		BoostStateLabel = IsBoostActive ? "^BoostOnLabel" : "^BoostOffLabel";
		BoostActionLabel = IsBoostActive ? "^BoostOffActionLabel" : "^BoostOnActionLabel";
		TemperatureUnits = temperatureUnits;
		IsScheduleAvailable = _platform?.HasHeatingSchedules == true;
		ScheduleSummary = BuildScheduleSummary (_room);
		ScheduleEnabled = IsScheduleEnabled (_room);
		CanEnableSchedule = !ScheduleEnabled && IsScheduleAvailable;
		CanDisableSchedule = ScheduleEnabled;
		ScheduleStatusLabel = BuildScheduleStatusLabel (_room);
		ScheduleToggleActionLabel = BuildScheduleToggleActionLabel (_room);
		SelectedScheduleName = BuildSelectedScheduleName (_room);
		RefreshScheduleValues (notify: true);
		ScheduleSelectorEnabled = _platform?.HasHeatingSchedules == true;
		CurrentTemperatureLabel = $"{CurrentTemperature:0.0}°";
		TileIcon = BuildTileIcon (_room);
		RefreshPropertyCache ();

		NotifyPropertyChanged ("deviceLabel", new DriverEntityValue (DeviceLabel));
		NotifyPropertyChanged ("currentTemperature", new DriverEntityValue (CurrentTemperature));
		NotifyPropertyChanged ("targetTemperature", new DriverEntityValue (TargetTemperature));
		NotifyPropertyChanged ("displayedSetpoint", new DriverEntityValue (DisplayedSetpoint));
		NotifyPropertyChanged ("isBoostActive", new DriverEntityValue (IsBoostActive));
		NotifyPropertyChanged ("boostStateLabel", new DriverEntityValue (BoostStateLabel));
		NotifyPropertyChanged ("boostActionLabel", new DriverEntityValue (BoostActionLabel));
		NotifyPropertyChanged ("temperatureUnits", new DriverEntityValue (TemperatureUnits));
		NotifyPropertyChanged ("isScheduleAvailable", new DriverEntityValue (IsScheduleAvailable));
		NotifyPropertyChanged ("scheduleSummary", new DriverEntityValue (ScheduleSummary));
		NotifyPropertyChanged ("scheduleEnabled", new DriverEntityValue (ScheduleEnabled));
		NotifyPropertyChanged ("canEnableSchedule", new DriverEntityValue (CanEnableSchedule));
		NotifyPropertyChanged ("canDisableSchedule", new DriverEntityValue (CanDisableSchedule));
		NotifyPropertyChanged ("scheduleStatusLabel", new DriverEntityValue (ScheduleStatusLabel));
		NotifyPropertyChanged ("scheduleToggleActionLabel", new DriverEntityValue (ScheduleToggleActionLabel));
		NotifyPropertyChanged ("selectedScheduleName", new DriverEntityValue (SelectedScheduleName));
		NotifyPropertyChanged ("selectedScheduleId", new DriverEntityValue (SelectedScheduleId));
		NotifyPropertyChanged ("scheduleSelectorEnabled", new DriverEntityValue (ScheduleSelectorEnabled));
		NotifyPropertyChanged ("currentTemperatureLabel", new DriverEntityValue (CurrentTemperatureLabel));
		NotifyPropertyChanged ("tileIcon", new DriverEntityValue (TileIcon));
		}

	public void SetOnline (bool online)
		{
		if (OnlineIndicatorIsOnline == online)
			return;

		OnlineIndicatorIsOnline = online;
		NotifyPropertyChanged ("onlineIndicator:isOnline", new DriverEntityValue (online));
		}

	public void StartPolling ()
		{
		if (Interlocked.CompareExchange (ref _frameworkReady, 1, 0) != 0)
			{
			LogInfo ("StartPolling ignored because framework is already ready");
			SetOnline (true);
			return;
			}

		LogInfo ("StartPolling initializing extension surface");

		if (_uiDefinition != null)
			{
			DriverEntityValue? uiValue = _uiDefinition.GetValue (null, null);
			if (uiValue.HasValue)
				NotifyPropertyChanged (UiDefinitionProperty.Name, uiValue.Value);
			}

		RefreshScheduleValues (notify: true);

		NotifyPropertyChanged ("readyIndicator:isReady", new DriverEntityValue (ReadyIndicatorIsReady));
		NotifyPropertyChanged ("deviceLabel", new DriverEntityValue (DeviceLabel));
		NotifyPropertyChanged ("currentTemperature", new DriverEntityValue (CurrentTemperature));
		NotifyPropertyChanged ("targetTemperature", new DriverEntityValue (TargetTemperature));
		NotifyPropertyChanged ("displayedSetpoint", new DriverEntityValue (DisplayedSetpoint));
		NotifyPropertyChanged ("isBoostActive", new DriverEntityValue (IsBoostActive));
		NotifyPropertyChanged ("boostStateLabel", new DriverEntityValue (BoostStateLabel));
		NotifyPropertyChanged ("boostActionLabel", new DriverEntityValue (BoostActionLabel));
		NotifyPropertyChanged ("temperatureUnits", new DriverEntityValue (TemperatureUnits));
		NotifyPropertyChanged ("isScheduleAvailable", new DriverEntityValue (IsScheduleAvailable));
		NotifyPropertyChanged ("scheduleSummary", new DriverEntityValue (ScheduleSummary));
		NotifyPropertyChanged ("scheduleEnabled", new DriverEntityValue (ScheduleEnabled));
		NotifyPropertyChanged ("canEnableSchedule", new DriverEntityValue (CanEnableSchedule));
		NotifyPropertyChanged ("canDisableSchedule", new DriverEntityValue (CanDisableSchedule));
		NotifyPropertyChanged ("scheduleStatusLabel", new DriverEntityValue (ScheduleStatusLabel));
		NotifyPropertyChanged ("scheduleToggleActionLabel", new DriverEntityValue (ScheduleToggleActionLabel));
		NotifyPropertyChanged ("selectedScheduleName", new DriverEntityValue (SelectedScheduleName));
		NotifyPropertyChanged ("selectedScheduleId", new DriverEntityValue (SelectedScheduleId));
		NotifyPropertyChanged ("scheduleSelectorEnabled", new DriverEntityValue (ScheduleSelectorEnabled));
		NotifyPropertyChanged ("currentTemperatureLabel", new DriverEntityValue (CurrentTemperatureLabel));
		NotifyPropertyChanged ("tileIcon", new DriverEntityValue (TileIcon));

		SetOnline (true);
		}

	public void StopPolling () => SetOnline (false);

	private async Task FireAndForgetAsync (Func<Task<bool>> action, string operation)
		{
		try
			{
			bool succeeded = await action ().ConfigureAwait (false);
			LogInfo ($"Operation '{operation}' completed with success={succeeded}");
			}
		catch (Exception ex)
			{
			_logger?.Log (
				_driverLogId,
				LogEntryLevel.Error,
				$"Failed to {operation} for room '{DeviceLabel}': {ex}");
			}
		}

	private string BuildScheduleSummary (WiserRoom room)
		{
		WiserHeatingSchedule assignedSchedule = ResolveAssignedSchedule (room);
		if (assignedSchedule == null)
			return IsScheduleEnabled (room) ? "Schedule available" : "No schedule assigned";

		string name = ResolveScheduleName (room, assignedSchedule);
		return $"{name} ({room.Mode})";
		}

	private bool IsScheduleEnabled (WiserRoom room)
		{
		if (room == null)
			return false;

		if (ResolveAssignedSchedule (room) == null)
			return false;

		if (string.IsNullOrWhiteSpace (room.Mode))
			return true;

		return room.Mode.IndexOf ("manual", StringComparison.OrdinalIgnoreCase) < 0;
		}

	private string BuildScheduleStatusLabel (WiserRoom room) =>
		IsScheduleEnabled (room) ? "^ScheduleEnabledLabel" : "^ScheduleDisabledLabel";

	private string BuildScheduleToggleActionLabel (WiserRoom room) =>
		IsScheduleEnabled (room) ? "^ScheduleDisableActionLabel" : "^ScheduleEnableActionLabel";

	private string BuildSelectedScheduleName (WiserRoom room)
		{
		if (ResolveAssignedSchedule (room) == null)
			return "^NoScheduleAssignedLabel";

		return ResolveScheduleName (room);
		}

	private string ResolveScheduleName (WiserRoom room, WiserHeatingSchedule assignedSchedule = null)
		{
		assignedSchedule ??= ResolveAssignedSchedule (room);

		if (!string.IsNullOrWhiteSpace (assignedSchedule?.Name))
			return assignedSchedule.Name;

		return $"Schedule {assignedSchedule?.Id ?? room?.ScheduleId ?? 0}";
		}

	private string BuildSelectedScheduleId (WiserRoom room)
		{
		WiserHeatingSchedule assignedSchedule = ResolveAssignedSchedule (room);
		if (assignedSchedule == null)
			return string.Empty;

		return assignedSchedule.Id.ToString ();
		}

	private WiserHeatingSchedule ResolveAssignedSchedule (WiserRoom room)
		{
		if (room == null)
			return null;

		return _platform?.GetAssignedScheduleForRoom (room.Id) ?? room.Schedule as WiserHeatingSchedule;
		}

	private void SelectSchedule (string scheduleIdText)
		{
		LogInfo ($"SelectSchedule requested with value='{scheduleIdText ?? string.Empty}'");

		if (_room == null || string.IsNullOrWhiteSpace (scheduleIdText))
			{
			LogInfo ("SelectSchedule ignored because room is null or value is empty");
			return;
			}

		int scheduleId = 0;
		foreach (WiserHeatingSchedule schedule in _platform?.HeatingSchedules ?? [])
			{
			if (string.Equals ((schedule?.Id ?? 0).ToString (), scheduleIdText, StringComparison.OrdinalIgnoreCase))
				{
				scheduleId = schedule.Id;
				break;
				}
			}

		if (scheduleId == 0 && !int.TryParse (scheduleIdText, out scheduleId))
			{
			LogInfo ($"SelectSchedule could not resolve value='{scheduleIdText}' to a schedule id");
			return;
			}

		LogInfo ($"SelectSchedule resolved value='{scheduleIdText}' to scheduleId={scheduleId}");

		_ = FireAndForgetAsync (
			() => _platform.SetRoomAssignedScheduleAsync (_room.Id, scheduleId),
			"select schedule");
		}

	private DriverEntityCommandResult SetExtensionPropertyValue (IDictionary<string, DriverEntityValue> args)
		{
		if (args == null)
			return new DriverEntityCommandResult (true, null);

		string propertyId = TryReadStringValue (args, "propertyId") ?? TryReadStringValue (args, "propertyName");
		string propertyValue = TryReadStringValue (args, "value") ?? TryReadStringValue (args, "propertyValue");

		LogInfo ($"SetExtensionPropertyValue propertyId='{propertyId ?? string.Empty}', value='{propertyValue ?? string.Empty}'");

		if (!string.IsNullOrWhiteSpace (propertyValue))
			SelectSchedule (propertyValue);

		return new DriverEntityCommandResult (false, null);
		}

	private void RefreshScheduleValues (bool notify)
		{
		string previousSelectedScheduleId = SelectedScheduleId ?? string.Empty;
		DriverEntityAvailableValue[] nextScheduleValues = BuildScheduleAvailableValues ();
		string nextSelectedScheduleId = BuildSelectedScheduleId (_room);

		ScheduleValues = nextScheduleValues;
		SelectedScheduleId = nextSelectedScheduleId;
		_propertyCache["selectedScheduleId"] = new DriverEntityValue (SelectedScheduleId ?? string.Empty);
		LogScheduleValues ($"RefreshScheduleValues(notify={notify})");
		RebuildSelectedScheduleProperty ();

		if (notify)
			{
			NotifyPropertyChanged (
				"selectedScheduleId",
				new DriverEntityValue (previousSelectedScheduleId),
				new DriverEntityValue (SelectedScheduleId ?? string.Empty));
			LogInfo ($"Published schedule selector state; newCount={ScheduleValues?.Length ?? 0}, previousSelectedScheduleId='{previousSelectedScheduleId}', newSelectedScheduleId='{SelectedScheduleId ?? string.Empty}'");
			}
		}

	private static string TryReadStringValue (IDictionary<string, DriverEntityValue> args, string key)
		{
		if (args == null || !args.TryGetValue (key, out DriverEntityValue value))
			return null;

		try
			{
			return value.GetValue<string> ();
			}
		catch
			{
			return null;
			}
		}

	private DriverEntityAvailableValue[] BuildScheduleAvailableValues ()
		{
		var values = new List<DriverEntityAvailableValue> ();
		var diagnostics = new List<string> ();
		foreach (WiserHeatingSchedule schedule in _platform?.HeatingSchedules ?? [])
			{
			string labelText = BuildScheduleOptionValue (schedule);
			string value = (schedule?.Id ?? 0).ToString ();
			// Crestron Home app renders SelectorButton available values using
			// LocalizationKey only, never falls back to Text. Setting both to
			// the display string ensures the label is visible even without a
			// translation dictionary entry.
			var label = new DriverEntityLocalizedString (labelText, labelText);
			diagnostics.Add ($"{value}:{labelText}(text='{label.Text}',key='{label.LocalizationKey ?? "<null>"}')");
			values.Add (new DriverEntityAvailableValue (
				value,
				label,
				false));
			}

		LogInfo ($"BuildScheduleAvailableValues produced count={values.Count}; schedules={(diagnostics.Count == 0 ? "<none>" : string.Join (", ", diagnostics))}");

		return [.. values];
		}

	private void LogScheduleValues (string context)
		{
		var diagnostics = new List<string> ();
		foreach (WiserHeatingSchedule schedule in _platform?.HeatingSchedules ?? [])
			diagnostics.Add ($"{schedule?.Id ?? 0}:{BuildScheduleOptionValue (schedule)}");

		WiserHeatingSchedule assignedSchedule = ResolveAssignedSchedule (_room);
		LogInfo ($"{context}; availableCount={_scheduleValues?.Length ?? 0}, selectedScheduleId='{SelectedScheduleId ?? string.Empty}', roomScheduleId={assignedSchedule?.Id ?? _room?.ScheduleId ?? 0}, roomScheduleName='{ResolveScheduleNameForLog (_room, assignedSchedule)}', platformSchedules={(diagnostics.Count == 0 ? "<none>" : string.Join (", ", diagnostics))}");
		}

	private void LogInfo (string message) =>
		_logger?.Log (_driverLogId, LogEntryLevel.Info, $"Room '{DeviceLabel ?? ControllerId}': {message}");

	private static string ResolveScheduleNameForLog (WiserRoom room, WiserHeatingSchedule assignedSchedule)
		{
		if (assignedSchedule == null)
			return string.Empty;

		if (!string.IsNullOrWhiteSpace (assignedSchedule.Name))
			return assignedSchedule.Name;

		return $"Schedule {assignedSchedule.Id}";
		}

	private static string BuildScheduleOptionValue (WiserHeatingSchedule schedule) =>
		string.IsNullOrWhiteSpace (schedule?.Name)
			? $"Schedule {schedule?.Id ?? 0}"
			: schedule.Name;

	private static bool IsRoomBoostActive (WiserRoom room)
		{
		if (room == null)
			return false;

		return room.BoostTimeRemaining > 0;
		}

	private void RebuildSelectedScheduleProperty ()
		{
		RemoveProperty ("selectedScheduleId");

		var noName = new DriverEntityLocalizedString (null, null);
		var writableMeta = new DriverEntityPropertyMetadata (true, true, false);
		var scheduleIdType = new DriverEntityTypeDefinition (
			DriverEntityValueType.String,
			DriverEntityValueType.Uninitialized,
			null,
			null,
			null,
			_scheduleValues,
			null);
		var selectedScheduleDefinition = new DriverEntityPropertyDefinition (
			noName,
			null,
			scheduleIdType,
			null,
			null,
			null,
			null);
		AddProperty (this, "selectedScheduleId", new DelegatePropertyInstance (
			selectedScheduleDefinition,
			writableMeta,
			(inst, lookup) =>
				{
				_ = _propertyCache.TryGetValue ("selectedScheduleId", out DriverEntityValue value);
				return value;
				}));
		RaiseDefinitionChangedEvent ();
		LogInfo ($"Rebuilt selectedScheduleId property definition with available value count={_scheduleValues?.Length ?? 0}");
		}

	private void RegisterExtensionSurface ()
		{
		var noName = new DriverEntityLocalizedString (null, null);
		var boolType = new DriverEntityTypeDefinition (DriverEntityValueType.Boolean, DriverEntityValueType.Uninitialized, null, null, null, null, null);
		var stringType = new DriverEntityTypeDefinition (DriverEntityValueType.String, DriverEntityValueType.Uninitialized, null, null, null, null, null);
		var tempRange = new DriverEntityValueRange (5.0, 35.0, 0.5);
		var tempType = new DriverEntityTypeDefinition (DriverEntityValueType.Number, DriverEntityValueType.Uninitialized, null, tempRange, null, null, null);
		var readableMeta = new DriverEntityPropertyMetadata (false, true, false);
		var writableMeta = new DriverEntityPropertyMetadata (true, true, false);

		RegisterCachedProperty ("currentTemperature", tempType, readableMeta, noName);
		RegisterCachedProperty ("targetTemperature", tempType, writableMeta, noName);
		RegisterCachedProperty ("displayedSetpoint", tempType, readableMeta, noName);
		RegisterCachedProperty ("deviceLabel", stringType, readableMeta, noName);
		RegisterCachedProperty ("boostStateLabel", stringType, readableMeta, noName);
		RegisterCachedProperty ("boostActionLabel", stringType, readableMeta, noName);
		RegisterCachedProperty ("temperatureUnits", stringType, readableMeta, noName);
		RegisterCachedProperty ("scheduleSummary", stringType, readableMeta, noName);
		RegisterCachedProperty ("scheduleToggleActionLabel", stringType, readableMeta, noName);
		RegisterCachedProperty ("scheduleStatusLabel", stringType, readableMeta, noName);
		RegisterCachedProperty ("selectedScheduleName", stringType, readableMeta, noName);
		RegisterCachedProperty ("currentTemperatureLabel", stringType, readableMeta, noName);
		RegisterCachedProperty ("tileIcon", stringType, readableMeta, noName);
		RegisterCachedProperty ("isBoostActive", boolType, readableMeta, noName);
		RegisterCachedProperty ("isScheduleAvailable", boolType, readableMeta, noName);
		RegisterCachedProperty ("scheduleEnabled", boolType, readableMeta, noName);
		RegisterCachedProperty ("canEnableSchedule", boolType, readableMeta, noName);
		RegisterCachedProperty ("canDisableSchedule", boolType, readableMeta, noName);
		RegisterCachedProperty ("scheduleSelectorEnabled", boolType, readableMeta, noName);
		RebuildSelectedScheduleProperty ();

		var noResult = new DriverEntityCommandResult (false, null);
		var commandMeta = new DriverEntityCommandMetadata (true, false);
		var emptyDef = new DriverEntityCommandDefinition (null, null, null, noName);

		AddCommand (this, "increaseSetpoint", new DelegateCommandInstance (
			"increaseSetpoint",
			emptyDef,
			commandMeta,
			(id, inst, args, lookup, cb) =>
				{
				IncreaseSetpoint ();
				cb?.Invoke (noResult);
				},
			null));

		AddCommand (this, "decreaseSetpoint", new DelegateCommandInstance (
			"decreaseSetpoint",
			emptyDef,
			commandMeta,
			(id, inst, args, lookup, cb) =>
				{
				DecreaseSetpoint ();
				cb?.Invoke (noResult);
				},
			null));

		AddCommand (this, "boost", new DelegateCommandInstance (
			"boost",
			emptyDef,
			commandMeta,
			(id, inst, args, lookup, cb) =>
				{
				Boost ();
				cb?.Invoke (noResult);
				},
			null));

		AddCommand (this, "openSchedule", new DelegateCommandInstance (
			"openSchedule",
			emptyDef,
			commandMeta,
			(id, inst, args, lookup, cb) =>
				{
				OpenSchedule ();
				cb?.Invoke (noResult);
				},
			null));

		AddCommand (this, "enableSchedule", new DelegateCommandInstance (
			"enableSchedule",
			emptyDef,
			commandMeta,
			(id, inst, args, lookup, cb) =>
				{
				EnableSchedule ();
				cb?.Invoke (noResult);
				},
			null));

		AddCommand (this, "disableSchedule", new DelegateCommandInstance (
			"disableSchedule",
			emptyDef,
			commandMeta,
			(id, inst, args, lookup, cb) =>
				{
				DisableSchedule ();
				cb?.Invoke (noResult);
				},
			null));

		AddCommand (this, ExtensionSetPropertyValueExecutor.CommandName, new DelegateCommandInstance (
			ExtensionSetPropertyValueExecutor.CommandName,
			ExtensionSetPropertyValueExecutor.CommandDefinition,
			commandMeta,
			(id, inst, args, lookup, cb) => cb?.Invoke (SetExtensionPropertyValue (args)),
			null));
		}

	private void RegisterCachedProperty (
		string propertyId,
		DriverEntityTypeDefinition typeDefinition,
		DriverEntityPropertyMetadata metadata,
		DriverEntityLocalizedString name)
		{
		var propertyDefinition = new DriverEntityPropertyDefinition (name, null, typeDefinition, null, null, null, null);
		AddProperty (this, propertyId, new DelegatePropertyInstance (
			propertyDefinition,
			metadata,
			(inst, lookup) =>
				{
				_ = _propertyCache.TryGetValue (propertyId, out DriverEntityValue value);
				return value;
				}));
		}

	private void RefreshPropertyCache ()
		{
		_propertyCache["currentTemperature"] = new DriverEntityValue (CurrentTemperature);
		_propertyCache["targetTemperature"] = new DriverEntityValue (TargetTemperature);
		_propertyCache["displayedSetpoint"] = new DriverEntityValue (DisplayedSetpoint);
		_propertyCache["deviceLabel"] = new DriverEntityValue (DeviceLabel ?? string.Empty);
		_propertyCache["boostStateLabel"] = new DriverEntityValue (BoostStateLabel ?? string.Empty);
		_propertyCache["boostActionLabel"] = new DriverEntityValue (BoostActionLabel ?? string.Empty);
		_propertyCache["temperatureUnits"] = new DriverEntityValue (TemperatureUnits ?? string.Empty);
		_propertyCache["scheduleSummary"] = new DriverEntityValue (ScheduleSummary ?? string.Empty);
		_propertyCache["scheduleToggleActionLabel"] = new DriverEntityValue (BuildScheduleToggleActionLabel (_room) ?? string.Empty);
		_propertyCache["scheduleStatusLabel"] = new DriverEntityValue (ScheduleStatusLabel ?? string.Empty);
		_propertyCache["selectedScheduleName"] = new DriverEntityValue (SelectedScheduleName ?? string.Empty);
		_propertyCache["selectedScheduleId"] = new DriverEntityValue (SelectedScheduleId ?? string.Empty);
		_propertyCache["currentTemperatureLabel"] = new DriverEntityValue (CurrentTemperatureLabel ?? string.Empty);
		_propertyCache["tileIcon"] = new DriverEntityValue (TileIcon ?? string.Empty);
		_propertyCache["isBoostActive"] = new DriverEntityValue (IsBoostActive);
		_propertyCache["isScheduleAvailable"] = new DriverEntityValue (IsScheduleAvailable);
		_propertyCache["scheduleEnabled"] = new DriverEntityValue (ScheduleEnabled);
		_propertyCache["canEnableSchedule"] = new DriverEntityValue (CanEnableSchedule);
		_propertyCache["canDisableSchedule"] = new DriverEntityValue (CanDisableSchedule);
		_propertyCache["scheduleSelectorEnabled"] = new DriverEntityValue (ScheduleSelectorEnabled);
		}

	private static string BuildTileIcon (WiserRoom room)
		{
		if (room?.IsHeating == true)
			return "icHeatingRegular";

		if (string.Equals (room?.TargetTemperatureOrigin, "FromSchedule", StringComparison.OrdinalIgnoreCase))
			return "icClimateSchedule";

		return "icClimateRegular";
		}

	}
