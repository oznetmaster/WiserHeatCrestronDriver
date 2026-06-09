// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
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

internal sealed class WiserRoomEntity : ReflectedAttributeDriverEntity
	{
	private int _frameworkReady;
	private readonly DriverControllerLogger _logger;
	private readonly string _driverLogId;
	private readonly WiserPlatformDriver _platform;
	private readonly UiDefinitionProperty _uiDefinition;
	private string _selectedScheduleId = string.Empty;
	private DriverEntityAvailableValue[] _scheduleValues = [];
	private WiserRoom _room;
	private const int MaxEditableScheduleSlots = 10;
	private static readonly string[] EditableScheduleDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
	private static readonly DriverEntityAvailableValue[] EditTimeValues = BuildEditTimeAvailableValues ();
	private readonly Dictionary<string, List<EditableScheduleSlot>> _editDaySlots = new (StringComparer.OrdinalIgnoreCase);
	private string _editSelectedDay = string.Empty;
	private readonly string[] _editSlotTimes = new string[MaxEditableScheduleSlots];
	private readonly double[] _editSlotTemperatures = new double[MaxEditableScheduleSlots];
	private readonly bool[] _editSlotVisible = new bool[MaxEditableScheduleSlots];
	private readonly string[] _editSlotErrors = new string[MaxEditableScheduleSlots];
	private IDictionary<string, object> _editScheduleData = new Dictionary<string, object> (StringComparer.OrdinalIgnoreCase);
	private int _editScheduleId;
	private string _editScheduleName = string.Empty;

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
		DeviceLabel = room.Name ?? controllerId;
		TargetTemperature = room.CurrentTargetTemperature;
		CurrentTemperature = room.CurrentTemperature;
		IsBoostActive = IsRoomBoostActive (room);
		BoostStateLabel = IsBoostActive ? "^BoostOnLabel" : "^BoostOffLabel";
		BoostActionLabel = IsBoostActive ? "^BoostOffActionLabel" : "^BoostOnActionLabel";
		TemperatureUnits = "Celsius";
		bool isScheduleAvailable = platform.HasHeatingSchedules;
		ScheduleSummary = BuildScheduleSummary (room);
		ScheduleEnabled = IsScheduleEnabled (room);
		CanEnableSchedule = !ScheduleEnabled && isScheduleAvailable;
		CanDisableSchedule = ScheduleEnabled;
		ScheduleStatusLabel = BuildScheduleStatusLabel (room);
		SelectedScheduleName = BuildSelectedScheduleName (room);
		SelectedScheduleId = BuildSelectedScheduleId (room);
		ScheduleSelectorEnabled = platform.HasHeatingSchedules;
		CurrentTemperatureLabel = $"{CurrentTemperature:0.0}°";
		TileIcon = BuildTileIcon (room);
		OnlineIndicatorIsOnline = true;
		ReadyIndicatorIsReady = true;
		EditScheduleSummary = string.Empty;
		EditScheduleImpact = string.Empty;
		EditScheduleError = string.Empty;
		EditSelectedDay = string.Empty;
		LoadEditScheduleState (notify: false);

		try
			{
			var roomRoot = Path.Combine (driverDataDirectoryPath, "room");
			_uiDefinition = UiDefinitionProperty.LoadFromDirectoryIfExists (roomRoot, resources.InitLogger, LogEntryLevel.Error)
				?? throw new InvalidOperationException ($"UiDefinition is required but was not found at '{roomRoot}'.");
			}
		catch (Exception ex)
			{
			_logger.Log (_driverLogId, LogEntryLevel.Error, "UiDefinition load failed: " + ex.Message);
			throw;
			}

		AddProperty (this, UiDefinitionProperty.Name, _uiDefinition);

		var doCommand = new ExtensionDoCommandExecutor (GetCommand, resources.Logger);
		AddCommand (this, ExtensionDoCommandExecutor.CommandName, doCommand);

		var setPropertyValue = new ExtensionSetPropertyValueExecutor (GetCommand, resources.Logger);
		AddCommand (this, ExtensionSetPropertyValueExecutor.CommandName, setPropertyValue);

		NotifyEditStateChanged ();
		}

	public DeviceUxCategory UxCategory => DeviceUxCategory.Thermostat;

	[EntityProperty (Id = "onlineIndicator:isOnline", Type = DriverEntityValueType.Boolean)]
	public bool OnlineIndicatorIsOnline
		{
		get;
		private set => SetAndNotify ("onlineIndicator:isOnline", value, ref field);
		}

	[EntityProperty (Id = "readyIndicator:isReady", Type = DriverEntityValueType.Boolean)]
	public bool ReadyIndicatorIsReady
		{
		get;
		private set => SetAndNotify ("readyIndicator:isReady", value, ref field);
		}

	[EntityProperty (Id = "deviceLabel", FriendlyName = "Room Label", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string DeviceLabel
		{
		get;
		private set => SetAndNotify ("deviceLabel", value, ref field);
		}

	[EntityProperty (Id = "currentTemperature", FriendlyName = "Current Temperature", Type = DriverEntityValueType.Number)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double CurrentTemperature
		{
		get;
		private set => SetAndNotify ("currentTemperature", value, ref field);
		}

	[EntityProperty (Id = "targetTemperature", FriendlyName = "Target Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double TargetTemperature
		{
		get;
		private set => SetAndNotify ("targetTemperature", value, ref field);
		}

	[EntityCommand (Id = "setTargetTemperature", FriendlyName = "Set Target Temperature")]
	public void SetTargetTemperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value)
		{
		LogInfo ($"UI requested targetTemperature value={value}");
		if (TargetTemperature.Equals (value))
			{
			LogInfo ($"UI targetTemperature ignored because value is unchanged ({value})");
			return;
			}

		TargetTemperature = value;
		_ = FireAndForgetAsync (
			() => _platform.SetRoomSetpointAsync (_room.Id, value),
			"set target temperature");
		}

	[EntityProperty (Id = "isBoostActive", FriendlyName = "Boost Active", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool IsBoostActive
		{
		get;
		private set => SetAndNotify ("isBoostActive", value, ref field);
		}

	[EntityProperty (Id = "boostStateLabel", FriendlyName = "Boost State Label", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string BoostStateLabel
		{
		get;
		private set => SetAndNotify ("boostStateLabel", value, ref field);
		}

	[EntityProperty (Id = "boostActionLabel", FriendlyName = "Boost Action Label", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string BoostActionLabel
		{
		get;
		private set => SetAndNotify ("boostActionLabel", value, ref field);
		}

	[EntityProperty (Id = "temperatureUnits", FriendlyName = "Temperature Units", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string TemperatureUnits
		{
		get;
		private set => SetAndNotify ("temperatureUnits", value, ref field);
		}

	[EntityProperty (Id = "scheduleSummary", FriendlyName = "Schedule Summary", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string ScheduleSummary
		{
		get;
		private set => SetAndNotify ("scheduleSummary", value, ref field);
		}

	[EntityProperty (Id = "scheduleEnabled", FriendlyName = "Schedule Enabled", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool ScheduleEnabled
		{
		get;
		private set => SetAndNotify ("scheduleEnabled", value, ref field);
		}

	[EntityProperty (Id = "canEnableSchedule", FriendlyName = "Can Enable Schedule", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool CanEnableSchedule
		{
		get;
		private set => SetAndNotify ("canEnableSchedule", value, ref field);
		}

	[EntityProperty (Id = "canDisableSchedule", FriendlyName = "Can Disable Schedule", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool CanDisableSchedule
		{
		get;
		private set => SetAndNotify ("canDisableSchedule", value, ref field);
		}

	[EntityProperty (Id = "scheduleStatusLabel", FriendlyName = "Schedule Status", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string ScheduleStatusLabel
		{
		get;
		private set => SetAndNotify ("scheduleStatusLabel", value, ref field);
		}

	[EntityProperty (Id = "selectedScheduleName", FriendlyName = "Selected Schedule", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string SelectedScheduleName
		{
		get;
		private set => SetAndNotify ("selectedScheduleName", value, ref field);
		}

	[EntityProperty (Id = "selectedScheduleOptions", FriendlyName = "Selected Schedule Options", Type = DriverEntityValueType.Array, ItemType = DriverEntityValueType.AvailableValue)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public DriverEntityAvailableValue[] SelectedScheduleOptions => ScheduleValues;

	[EntityProperty (Id = "selectedScheduleId", FriendlyName = "Selected Schedule Id", Type = DriverEntityValueType.String, AvailableValuesProperty = "selectedScheduleOptions")]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string SelectedScheduleId
		{
		get => _selectedScheduleId;
		private set => SetAndNotify ("selectedScheduleId", value ?? string.Empty, ref _selectedScheduleId);
		}

	[EntityCommand (Id = "setSelectedScheduleId", FriendlyName = "Set Selected Schedule Id")]
	public void SetSelectedScheduleId (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value)
		{
		string selectedScheduleId = value ?? string.Empty;
		LogInfo ($"UI requested selectedScheduleId='{selectedScheduleId}'");
		if (string.Equals (SelectedScheduleId, selectedScheduleId, StringComparison.Ordinal))
			{
			LogInfo ($"UI selectedScheduleId ignored because value is unchanged ('{selectedScheduleId}')");
			return;
			}

		SelectedScheduleId = selectedScheduleId;
		SelectSchedule (selectedScheduleId);
		}

	private DriverEntityAvailableValue[] ScheduleValues
		{
		get => _scheduleValues;
		set => _scheduleValues = value ?? [];
		}

	[EntityProperty (Id = "scheduleSelectorEnabled", FriendlyName = "Schedule Selector Enabled", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool ScheduleSelectorEnabled
		{
		get;
		private set => SetAndNotify ("scheduleSelectorEnabled", value, ref field);
		}

	[EntityProperty (Id = "currentTemperatureLabel", FriendlyName = "Current Temperature Label", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string CurrentTemperatureLabel
		{
		get;
		private set => SetAndNotify ("currentTemperatureLabel", value, ref field);
		}

	[EntityProperty (Id = "tileIcon", FriendlyName = "Tile Icon", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string TileIcon
		{
		get;
		private set => SetAndNotify ("tileIcon", value, ref field);
		}

	[EntityProperty (Id = "editScheduleEnabled", FriendlyName = "Edit Schedule Enabled", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditScheduleEnabled
		{
		get;
		private set => SetAndNotify ("editScheduleEnabled", value, ref field);
		}

	[EntityProperty (Id = "editScheduleSummary", FriendlyName = "Edit Schedule Summary", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditScheduleSummary
		{
		get;
		private set => SetAndNotify ("editScheduleSummary", value, ref field);
		}

	[EntityProperty (Id = "editScheduleImpact", FriendlyName = "Edit Schedule Impact", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditScheduleImpact
		{
		get;
		private set => SetAndNotify ("editScheduleImpact", value, ref field);
		}

	[EntityProperty (Id = "editScheduleError", FriendlyName = "Edit Schedule Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditScheduleError
		{
		get;
		private set => SetAndNotify ("editScheduleError", value, ref field);
		}

	[EntityProperty (
		Id = "editSelectedDay",
		FriendlyName = "Edit Selected Day",
		Type = DriverEntityValueType.String,
		AvailableValues = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
		// Crestron selectorbutton uses the localization key for selector labels and caches the initial caption width; keep keys equal to labels and pad shorter day names to avoid truncation.
		AvailableValuesLabels = new[] { "    Sunday    ", "    Monday    ", "   Tuesday    ", "Wednesday", "   Thursday   ", "    Friday    ", "   Saturday   " },
		AvailableValuesLocalizationKeys = new[] { "    Sunday    ", "    Monday    ", "   Tuesday    ", "Wednesday", "   Thursday   ", "    Friday    ", "   Saturday   " })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSelectedDay
		{
		get => _editSelectedDay;
		private set => SetAndNotify ("editSelectedDay", value ?? string.Empty, ref _editSelectedDay);
		}

	[EntityCommand (Id = "setEditSelectedDay", FriendlyName = "Set Edit Selected Day")]
	public void SetEditSelectedDay (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSelectedDayFromUi (value ?? string.Empty);

	[EntityProperty (
		Id = "editSlot1Time",
		FriendlyName = "Edit Slot 1 Time",
		Type = DriverEntityValueType.String,
		// Crestron selectorbutton renders selector labels from LocalizationKey instead of Text; keep time values, labels, and keys identical.
		AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" },
		AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" },
		AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot1Time
		{
		get => _editSlotTimes[0] ?? string.Empty;
		private set => _editSlotTimes[0] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot2Time", FriendlyName = "Edit Slot 2 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot2Time
		{
		get => _editSlotTimes[1] ?? string.Empty;
		private set => _editSlotTimes[1] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot3Time", FriendlyName = "Edit Slot 3 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot3Time
		{
		get => _editSlotTimes[2] ?? string.Empty;
		private set => _editSlotTimes[2] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot4Time", FriendlyName = "Edit Slot 4 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot4Time
		{
		get => _editSlotTimes[3] ?? string.Empty;
		private set => _editSlotTimes[3] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot5Time", FriendlyName = "Edit Slot 5 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot5Time
		{
		get => _editSlotTimes[4] ?? string.Empty;
		private set => _editSlotTimes[4] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot6Time", FriendlyName = "Edit Slot 6 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot6Time
		{
		get => _editSlotTimes[5] ?? string.Empty;
		private set => _editSlotTimes[5] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot7Time", FriendlyName = "Edit Slot 7 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot7Time
		{
		get => _editSlotTimes[6] ?? string.Empty;
		private set => _editSlotTimes[6] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot8Time", FriendlyName = "Edit Slot 8 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot8Time
		{
		get => _editSlotTimes[7] ?? string.Empty;
		private set => _editSlotTimes[7] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot9Time", FriendlyName = "Edit Slot 9 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot9Time
		{
		get => _editSlotTimes[8] ?? string.Empty;
		private set => _editSlotTimes[8] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot10Time", FriendlyName = "Edit Slot 10 Time", Type = DriverEntityValueType.String, AvailableValues = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLabels = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" }, AvailableValuesLocalizationKeys = new[] { "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00", "03:30", "04:00", "04:30", "05:00", "05:30", "06:00", "06:30", "07:00", "07:30", "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00", "21:30", "22:00", "22:30", "23:00", "23:30" })]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot10Time
		{
		get => _editSlotTimes[9] ?? string.Empty;
		private set => _editSlotTimes[9] = value ?? string.Empty;
		}

	[EntityProperty (Id = "editSlot1Temperature", FriendlyName = "Edit Slot 1 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot1Temperature
		{
		get => _editSlotTemperatures[0];
		private set => _editSlotTemperatures[0] = value;
		}

	[EntityProperty (Id = "editSlot2Temperature", FriendlyName = "Edit Slot 2 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot2Temperature
		{
		get => _editSlotTemperatures[1];
		private set => _editSlotTemperatures[1] = value;
		}

	[EntityProperty (Id = "editSlot3Temperature", FriendlyName = "Edit Slot 3 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot3Temperature
		{
		get => _editSlotTemperatures[2];
		private set => _editSlotTemperatures[2] = value;
		}

	[EntityProperty (Id = "editSlot4Temperature", FriendlyName = "Edit Slot 4 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot4Temperature
		{
		get => _editSlotTemperatures[3];
		private set => _editSlotTemperatures[3] = value;
		}

	[EntityProperty (Id = "editSlot5Temperature", FriendlyName = "Edit Slot 5 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot5Temperature
		{
		get => _editSlotTemperatures[4];
		private set => _editSlotTemperatures[4] = value;
		}

	[EntityProperty (Id = "editSlot6Temperature", FriendlyName = "Edit Slot 6 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot6Temperature
		{
		get => _editSlotTemperatures[5];
		private set => _editSlotTemperatures[5] = value;
		}

	[EntityProperty (Id = "editSlot7Temperature", FriendlyName = "Edit Slot 7 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot7Temperature
		{
		get => _editSlotTemperatures[6];
		private set => _editSlotTemperatures[6] = value;
		}

	[EntityProperty (Id = "editSlot8Temperature", FriendlyName = "Edit Slot 8 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot8Temperature
		{
		get => _editSlotTemperatures[7];
		private set => _editSlotTemperatures[7] = value;
		}

	[EntityProperty (Id = "editSlot9Temperature", FriendlyName = "Edit Slot 9 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot9Temperature
		{
		get => _editSlotTemperatures[8];
		private set => _editSlotTemperatures[8] = value;
		}

	[EntityProperty (Id = "editSlot10Temperature", FriendlyName = "Edit Slot 10 Temperature", Type = DriverEntityValueType.Number, RangeMinimum = 5.0, RangeMaximum = 35.0, RangeStepSize = 0.5)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public double EditSlot10Temperature
		{
		get => _editSlotTemperatures[9];
		private set => _editSlotTemperatures[9] = value;
		}

	[EntityCommand (Id = "setEditSlot1Time", FriendlyName = "Set Edit Slot 1 Time")]
	public void SetEditSlot1Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (0, "editSlot1Time", value);

	[EntityCommand (Id = "setEditSlot2Time", FriendlyName = "Set Edit Slot 2 Time")]
	public void SetEditSlot2Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (1, "editSlot2Time", value);

	[EntityCommand (Id = "setEditSlot3Time", FriendlyName = "Set Edit Slot 3 Time")]
	public void SetEditSlot3Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (2, "editSlot3Time", value);

	[EntityCommand (Id = "setEditSlot4Time", FriendlyName = "Set Edit Slot 4 Time")]
	public void SetEditSlot4Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (3, "editSlot4Time", value);

	[EntityCommand (Id = "setEditSlot5Time", FriendlyName = "Set Edit Slot 5 Time")]
	public void SetEditSlot5Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (4, "editSlot5Time", value);

	[EntityCommand (Id = "setEditSlot6Time", FriendlyName = "Set Edit Slot 6 Time")]
	public void SetEditSlot6Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (5, "editSlot6Time", value);

	[EntityCommand (Id = "setEditSlot7Time", FriendlyName = "Set Edit Slot 7 Time")]
	public void SetEditSlot7Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (6, "editSlot7Time", value);

	[EntityCommand (Id = "setEditSlot8Time", FriendlyName = "Set Edit Slot 8 Time")]
	public void SetEditSlot8Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (7, "editSlot8Time", value);

	[EntityCommand (Id = "setEditSlot9Time", FriendlyName = "Set Edit Slot 9 Time")]
	public void SetEditSlot9Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (8, "editSlot9Time", value);

	[EntityCommand (Id = "setEditSlot10Time", FriendlyName = "Set Edit Slot 10 Time")]
	public void SetEditSlot10Time (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.String)]
		string value) => SetEditSlotTimeProperty (9, "editSlot10Time", value);

	[EntityCommand (Id = "setEditSlot1Temperature", FriendlyName = "Set Edit Slot 1 Temperature")]
	public void SetEditSlot1Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (0, "editSlot1Temperature", value);

	[EntityCommand (Id = "setEditSlot2Temperature", FriendlyName = "Set Edit Slot 2 Temperature")]
	public void SetEditSlot2Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (1, "editSlot2Temperature", value);

	[EntityCommand (Id = "setEditSlot3Temperature", FriendlyName = "Set Edit Slot 3 Temperature")]
	public void SetEditSlot3Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (2, "editSlot3Temperature", value);

	[EntityCommand (Id = "setEditSlot4Temperature", FriendlyName = "Set Edit Slot 4 Temperature")]
	public void SetEditSlot4Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (3, "editSlot4Temperature", value);

	[EntityCommand (Id = "setEditSlot5Temperature", FriendlyName = "Set Edit Slot 5 Temperature")]
	public void SetEditSlot5Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (4, "editSlot5Temperature", value);

	[EntityCommand (Id = "setEditSlot6Temperature", FriendlyName = "Set Edit Slot 6 Temperature")]
	public void SetEditSlot6Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (5, "editSlot6Temperature", value);

	[EntityCommand (Id = "setEditSlot7Temperature", FriendlyName = "Set Edit Slot 7 Temperature")]
	public void SetEditSlot7Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (6, "editSlot7Temperature", value);

	[EntityCommand (Id = "setEditSlot8Temperature", FriendlyName = "Set Edit Slot 8 Temperature")]
	public void SetEditSlot8Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (7, "editSlot8Temperature", value);

	[EntityCommand (Id = "setEditSlot9Temperature", FriendlyName = "Set Edit Slot 9 Temperature")]
	public void SetEditSlot9Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (8, "editSlot9Temperature", value);

	[EntityCommand (Id = "setEditSlot10Temperature", FriendlyName = "Set Edit Slot 10 Temperature")]
	public void SetEditSlot10Temperature (
		[EntityParameter (Id = "value", Type = DriverEntityValueType.Number)]
		double value) => SetEditSlotTemperatureProperty (9, "editSlot10Temperature", value);

	[EntityProperty (Id = "editSlot1Visible", FriendlyName = "Edit Slot 1 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot1Visible => _editSlotVisible[0];

	[EntityProperty (Id = "editSlot2Visible", FriendlyName = "Edit Slot 2 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot2Visible => _editSlotVisible[1];

	[EntityProperty (Id = "editSlot3Visible", FriendlyName = "Edit Slot 3 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot3Visible => _editSlotVisible[2];

	[EntityProperty (Id = "editSlot4Visible", FriendlyName = "Edit Slot 4 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot4Visible => _editSlotVisible[3];

	[EntityProperty (Id = "editSlot5Visible", FriendlyName = "Edit Slot 5 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot5Visible => _editSlotVisible[4];

	[EntityProperty (Id = "editSlot6Visible", FriendlyName = "Edit Slot 6 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot6Visible => _editSlotVisible[5];

	[EntityProperty (Id = "editSlot7Visible", FriendlyName = "Edit Slot 7 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot7Visible => _editSlotVisible[6];

	[EntityProperty (Id = "editSlot8Visible", FriendlyName = "Edit Slot 8 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot8Visible => _editSlotVisible[7];

	[EntityProperty (Id = "editSlot9Visible", FriendlyName = "Edit Slot 9 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot9Visible => _editSlotVisible[8];

	[EntityProperty (Id = "editSlot10Visible", FriendlyName = "Edit Slot 10 Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool EditSlot10Visible => _editSlotVisible[9];

	[EntityProperty (Id = "editSlot1Error", FriendlyName = "Edit Slot 1 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot1Error => _editSlotErrors[0] ?? string.Empty;

	[EntityProperty (Id = "editSlot2Error", FriendlyName = "Edit Slot 2 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot2Error => _editSlotErrors[1] ?? string.Empty;

	[EntityProperty (Id = "editSlot3Error", FriendlyName = "Edit Slot 3 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot3Error => _editSlotErrors[2] ?? string.Empty;

	[EntityProperty (Id = "editSlot4Error", FriendlyName = "Edit Slot 4 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot4Error => _editSlotErrors[3] ?? string.Empty;

	[EntityProperty (Id = "editSlot5Error", FriendlyName = "Edit Slot 5 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot5Error => _editSlotErrors[4] ?? string.Empty;

	[EntityProperty (Id = "editSlot6Error", FriendlyName = "Edit Slot 6 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot6Error => _editSlotErrors[5] ?? string.Empty;

	[EntityProperty (Id = "editSlot7Error", FriendlyName = "Edit Slot 7 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot7Error => _editSlotErrors[6] ?? string.Empty;

	[EntityProperty (Id = "editSlot8Error", FriendlyName = "Edit Slot 8 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot8Error => _editSlotErrors[7] ?? string.Empty;

	[EntityProperty (Id = "editSlot9Error", FriendlyName = "Edit Slot 9 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot9Error => _editSlotErrors[8] ?? string.Empty;

	[EntityProperty (Id = "editSlot10Error", FriendlyName = "Edit Slot 10 Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string EditSlot10Error => _editSlotErrors[9] ?? string.Empty;


	[EntityCommand (Id = "boost", FriendlyName = "Boost")]
	[EntityCommandMetadata (Programmable = true)]
	public void Boost ()
		{
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
		_ = FireAndForgetAsync (
			() => _platform.SetRoomScheduleEnabledAsync (_room.Id, true),
			"enable schedule");
		}

	[EntityCommand (Id = "disableSchedule", FriendlyName = "Disable Schedule")]
	[EntityCommandMetadata (Programmable = true)]
	public void DisableSchedule ()
		{
		_ = FireAndForgetAsync (
			() => _platform.SetRoomScheduleEnabledAsync (_room.Id, false),
			"disable schedule");
		}

	[EntityCommand (Id = "openEditSchedule", FriendlyName = "Open Edit Schedule")]
	[EntityCommandMetadata (Programmable = true)]
	public void OpenEditSchedule ()
		{
		LogInfo ($"OpenEditSchedule invoked; selectedScheduleId='{SelectedScheduleId ?? string.Empty}'");
		LoadEditScheduleState (notify: _frameworkReady != 0);
		}

	[EntityCommand (Id = "saveEditScheduleDay", FriendlyName = "Save Edit Schedule Day")]
	[EntityCommandMetadata (Programmable = true)]
	public void SaveEditScheduleDay () =>
		_ = FireAndForgetAsync (
			() => SaveEditedScheduleAsync (applyToAllDays: false),
			"save schedule day");

	[EntityCommand (Id = "saveEditScheduleAllDays", FriendlyName = "Save Edit Schedule All Days")]
	[EntityCommandMetadata (Programmable = true)]
	public void SaveEditScheduleAllDays () =>
		_ = FireAndForgetAsync (
			() => SaveEditedScheduleAsync (applyToAllDays: true),
			"save schedule all days");

	[EntityCommand (Id = "cancelEditSchedule", FriendlyName = "Cancel Edit Schedule")]
	[EntityCommandMetadata (Programmable = true)]
	public void CancelEditSchedule ()
		{
		LogInfo ("CancelEditSchedule invoked");
		LoadEditScheduleState (notify: _frameworkReady != 0);
		}

	public void UpdateFromRoom (WiserRoom room, string? temperatureUnits)
		{
		_room = room;

		WiserHeatingSchedule? assignedSchedule = ResolveAssignedSchedule (_room);
		LogInfo ($"UpdateFromRoom start; roomId={_room.Id}, mode='{_room.Mode ?? string.Empty}', scheduleId={assignedSchedule?.Id ?? _room.ScheduleId}, scheduleName='{ResolveScheduleNameForLog (_room, assignedSchedule)}', temperatureUnits='{temperatureUnits ?? string.Empty}'");

		DeviceLabel = _room.Name ?? DeviceLabel;
		CurrentTemperature = _room.CurrentTemperature;
		TargetTemperature = _room.CurrentTargetTemperature;
		IsBoostActive = IsRoomBoostActive (_room);
		BoostStateLabel = IsBoostActive ? "^BoostOnLabel" : "^BoostOffLabel";
		BoostActionLabel = IsBoostActive ? "^BoostOffActionLabel" : "^BoostOnActionLabel";
		TemperatureUnits = temperatureUnits ?? string.Empty;
		bool isScheduleAvailable = _platform.HasHeatingSchedules;
		ScheduleSummary = BuildScheduleSummary (_room);
		ScheduleEnabled = IsScheduleEnabled (_room);
		CanEnableSchedule = !ScheduleEnabled && isScheduleAvailable;
		CanDisableSchedule = ScheduleEnabled;
		ScheduleStatusLabel = BuildScheduleStatusLabel (_room);
		SelectedScheduleName = BuildSelectedScheduleName (_room);
		RefreshScheduleValues (notify: true);
		ScheduleSelectorEnabled = _platform.HasHeatingSchedules;
		CurrentTemperatureLabel = $"{CurrentTemperature:0.0}°";
		TileIcon = BuildTileIcon (_room);
		LoadEditScheduleState (notify: _frameworkReady != 0);
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
		SetOnline (true);

		DriverEntityValue? uiValue = _uiDefinition.GetValue (null, null);
		if (uiValue.HasValue)
			NotifyPropertyChanged (UiDefinitionProperty.Name, uiValue.Value);

		RefreshScheduleValues (notify: true);
		LoadEditScheduleState (notify: true);
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
			_logger.Log (
				_driverLogId,
				LogEntryLevel.Error,
				$"Failed to {operation} for room '{DeviceLabel}': {ex}");
			}
		}

	private string BuildScheduleSummary (WiserRoom room)
		{
		WiserHeatingSchedule? assignedSchedule = ResolveAssignedSchedule (room);
		if (assignedSchedule == null)
			return IsScheduleEnabled (room) ? "Schedule available" : "No schedule assigned";

		string name = ResolveScheduleName (room, assignedSchedule);
		return $"{name} ({room.Mode ?? string.Empty})";
		}

	private bool IsScheduleEnabled (WiserRoom room)
		{
		if (ResolveAssignedSchedule (room) == null)
			return false;

		if (string.IsNullOrWhiteSpace (room.Mode))
			return true;

		return room.Mode.IndexOf ("manual", StringComparison.OrdinalIgnoreCase) < 0;
		}

	private string BuildScheduleStatusLabel (WiserRoom room) =>
		IsScheduleEnabled (room) ? "^ScheduleEnabledLabel" : "^ScheduleDisabledLabel";

	private string BuildSelectedScheduleName (WiserRoom room)
		{
		if (ResolveAssignedSchedule (room) == null)
			return "^NoScheduleAssignedLabel";

		return ResolveScheduleName (room);
		}

	private string ResolveScheduleName (WiserRoom room, WiserHeatingSchedule? assignedSchedule = null)
		{
		assignedSchedule ??= ResolveAssignedSchedule (room);

		if (!string.IsNullOrWhiteSpace (assignedSchedule?.Name))
			return assignedSchedule!.Name!;

		return $"Schedule {assignedSchedule?.Id ?? room.ScheduleId}";
		}

	private string BuildSelectedScheduleId (WiserRoom room)
		{
		WiserHeatingSchedule? assignedSchedule = ResolveAssignedSchedule (room);
		if (assignedSchedule == null)
			return string.Empty;

		return assignedSchedule.Id.ToString ();
		}

	private WiserHeatingSchedule? ResolveAssignedSchedule (WiserRoom room)
		{
		return _platform.GetAssignedScheduleForRoom (room.Id) ?? room.Schedule as WiserHeatingSchedule;
		}

	private void SelectSchedule (string scheduleIdText)
		{
		LogInfo ($"SelectSchedule requested with value='{scheduleIdText ?? string.Empty}'");

		if (string.IsNullOrWhiteSpace (scheduleIdText))
			{
			LogInfo ("SelectSchedule ignored because value is empty");
			return;
			}

		int scheduleId = 0;
		foreach (WiserHeatingSchedule schedule in _platform.HeatingSchedules ?? [])
			{
			if (string.Equals ((schedule?.Id ?? 0).ToString (), scheduleIdText, StringComparison.OrdinalIgnoreCase))
				{
					scheduleId = schedule!.Id;
				break;
				}
			}

		if (scheduleId == 0 && !int.TryParse (scheduleIdText, out scheduleId))
			{
			LogInfo ($"SelectSchedule could not resolve value='{scheduleIdText}' to a schedule id");
			return;
			}

		LogInfo ($"SelectSchedule resolved value='{scheduleIdText}' to scheduleId={scheduleId}");

		int roomId = _room.Id;
		_ = FireAndForgetAsync (
			() => AssignSelectedScheduleAsync (roomId, scheduleId),
			"select schedule");
		}

	private Task<bool> AssignSelectedScheduleAsync (int roomId, int scheduleId) =>
		_platform.SetRoomAssignedScheduleAsync (roomId, scheduleId);

	private void RefreshScheduleValues (bool notify)
		{
		string previousSelectedScheduleId = SelectedScheduleId ?? string.Empty;
		DriverEntityAvailableValue[] nextScheduleValues = BuildScheduleAvailableValues ();
		string nextSelectedScheduleId = BuildSelectedScheduleId (_room);

		ScheduleValues = nextScheduleValues;
		SelectedScheduleId = nextSelectedScheduleId;
		LogScheduleValues ($"RefreshScheduleValues(notify={notify})");

		if (notify)
			{
			NotifyPropertyChanged ("selectedScheduleOptions", new DriverEntityValue (SelectedScheduleOptions));
			LogInfo ($"Published schedule selector state; newCount={ScheduleValues?.Length ?? 0}, previousSelectedScheduleId='{previousSelectedScheduleId}', newSelectedScheduleId='{SelectedScheduleId ?? string.Empty}'");
			}
		}

	private void LoadEditScheduleState (bool notify)
		{
		WiserHeatingSchedule? assignedSchedule = ResolveAssignedSchedule (_room);
		_editDaySlots.Clear ();
		_editScheduleData = new Dictionary<string, object> (StringComparer.OrdinalIgnoreCase);
		_editScheduleId = assignedSchedule?.Id ?? 0;
		_editScheduleName = BuildScheduleOptionValue (assignedSchedule);
		EditScheduleError = string.Empty;
		EditScheduleEnabled = assignedSchedule != null;

		if (assignedSchedule != null)
			{
			_editScheduleData = CloneScheduleData (assignedSchedule.ScheduleData);
			foreach (string day in EditableScheduleDays)
				{
				if (_editScheduleData.TryGetValue (day, out object daySchedule))
					_editDaySlots[day] = BuildEditableSlots (daySchedule);
				}
			EditSelectedDay = ResolveInitialEditDay (EditSelectedDay);
			LoadSelectedEditDay (EditSelectedDay);
			EditScheduleImpact = BuildEditScheduleImpact (assignedSchedule);
			}
		else
			{
			EditSelectedDay = string.Empty;
			ResetEditSlotState ();
			EditScheduleImpact = string.Empty;
			}

		EditScheduleSummary = BuildEditScheduleSummary ();

		if (notify)
			NotifyEditStateChanged ();
		}

	private void SetEditSlotTimeProperty (int slotIndex, string propertyId, string value)
		{
		string timeValue = value ?? string.Empty;
		LogInfo ($"UI requested {propertyId}='{timeValue}'");
		UpdateEditSlotTime (slotIndex, timeValue);
		}

	private async Task<bool> SaveEditedScheduleAsync (bool applyToAllDays)
		{
		if (_editScheduleId == 0)
			{
			SetEditError ("No schedule assigned", notify: true);
			return false;
			}

		if (!TryBuildEditedDaySchedule (out Dictionary<string, object>? editedDaySchedule, out string errorMessage))
			{
			SetEditError (errorMessage, notify: true);
			return false;
			}

		editedDaySchedule ??= new Dictionary<string, object> (StringComparer.OrdinalIgnoreCase);

		LogInfo ($"SaveEditedScheduleAsync prepared selectedDay='{EditSelectedDay}', applyToAllDays={applyToAllDays}, editedDay={DescribeDaySchedule (editedDaySchedule)}");

		IDictionary<string, object> scheduleData = CloneScheduleData (_editScheduleData);
		if (applyToAllDays)
			{
			foreach (string day in EditableScheduleDays)
				scheduleData[day] = CloneScheduleValue (editedDaySchedule);
			}
		else
			{
			scheduleData[EditSelectedDay] = editedDaySchedule;
			}

		SetEditError (string.Empty, notify: true);
		bool saved = await _platform.SaveHeatingScheduleAsync (_editScheduleId, scheduleData).ConfigureAwait (false);
		if (!saved)
			{
			SetEditError ("Unable to save schedule changes", notify: true);
			return false;
			}

		LoadEditScheduleState (notify: _frameworkReady != 0);
		return true;
		}

	private void SetEditSelectedDayFromUi (string day)
		{
		LogInfo ($"UI requested editSelectedDay='{day ?? string.Empty}'");
		if (string.IsNullOrWhiteSpace (day))
			{
			LogInfo ("UI editSelectedDay ignored because value is empty");
			return;
			}

		string normalizedDay = ResolveInitialEditDay (day ?? string.Empty);
		if (string.IsNullOrWhiteSpace (normalizedDay))
			{
			LogInfo ($"UI editSelectedDay ignored because '{day}' could not be normalized");
			return;
			}

		if (!SetValue (normalizedDay, ref _editSelectedDay))
			{
			LogInfo ($"UI editSelectedDay ignored because normalized value is unchanged ('{normalizedDay}')");
			return;
			}

		EditScheduleError = string.Empty;
		LoadSelectedEditDay (normalizedDay);
		EditScheduleSummary = BuildEditScheduleSummary ();
		LogInfo ($"UI editSelectedDay accepted; normalized='{normalizedDay}'");
		}

	private void SetEditSlotTemperatureProperty (int slotIndex, string propertyId, double value)
		{
		LogInfo ($"UI requested {propertyId}={value.ToString (CultureInfo.InvariantCulture)}");
		UpdateEditSlotTemperature (slotIndex, value.ToString (CultureInfo.InvariantCulture));
		}

	private void LoadSelectedEditDay (string day)
		{
		ResetEditSlotState ();
		if (string.IsNullOrWhiteSpace (day) || !_editDaySlots.TryGetValue (day, out List<EditableScheduleSlot> slots))
			return;

		for (int i = 0; i < slots.Count && i < MaxEditableScheduleSlots; i++)
			{
			_editSlotVisible[i] = true;
			_editSlotTimes[i] = slots[i].Time;
			_editSlotTemperatures[i] = slots[i].Temperature;
			_editSlotErrors[i] = string.Empty;
			}
		}

	private void UpdateEditSlotTime (int slotIndex, string timeText)
		{
		if (!TryGetEditableSlot (slotIndex, out EditableScheduleSlot? slot) || slot == null)
			{
			LogInfo ($"Ignored edit slot time update for slot={slotIndex + 1} because no editable slot is loaded for day '{EditSelectedDay}'");
			return;
			}

		string normalizedTime = NormalizeTimeText (timeText);
		if (!EditTimeValues.Any (value => string.Equals (value.Value, normalizedTime, StringComparison.OrdinalIgnoreCase)))
			{
			LogInfo ($"Ignored edit slot time update for slot={slotIndex + 1} because '{normalizedTime}' is not an allowed value");
			return;
			}

		if (string.Equals (_editSlotTimes[slotIndex], normalizedTime, StringComparison.Ordinal))
			{
			LogInfo ($"Ignored edit slot time update for slot={slotIndex + 1} because value is unchanged ('{normalizedTime}')");
			return;
			}

		slot.Time = normalizedTime;
		_editSlotTimes[slotIndex] = slot.Time;
		string slotErrorPropertyId = GetEditSlotErrorPropertyId (slotIndex);
		string previousSlotError = _editSlotErrors[slotIndex] ?? string.Empty;
		_editSlotErrors[slotIndex] = string.Empty;
		LogInfo ($"Updated edit slot time slot={slotIndex + 1}, day='{EditSelectedDay}', value='{slot.Time}'");
		EditScheduleError = string.Empty;
		if (!string.IsNullOrEmpty (previousSlotError))
			NotifyPropertyChanged (slotErrorPropertyId, new DriverEntityValue (string.Empty));
		}

	private void UpdateEditSlotTemperature (int slotIndex, string temperatureText)
		{
		if (!TryGetEditableSlot (slotIndex, out EditableScheduleSlot? slot) || slot == null)
			{
			LogInfo ($"Ignored edit slot temperature update for slot={slotIndex + 1} because no editable slot is loaded for day '{EditSelectedDay}'");
			return;
			}

		if (!double.TryParse (temperatureText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
			{
			LogInfo ($"Ignored edit slot temperature update for slot={slotIndex + 1} because '{temperatureText}' could not be parsed");
			return;
			}

		parsed = Math.Round (parsed * 2.0, MidpointRounding.AwayFromZero) / 2.0;
		if (_editSlotTemperatures[slotIndex].Equals (parsed))
			{
			LogInfo ($"Ignored edit slot temperature update for slot={slotIndex + 1} because value is unchanged ({parsed.ToString (CultureInfo.InvariantCulture)})");
			return;
			}

		slot.Temperature = parsed;
		_editSlotTemperatures[slotIndex] = parsed;
		string slotErrorPropertyId = GetEditSlotErrorPropertyId (slotIndex);
		string previousSlotError = _editSlotErrors[slotIndex] ?? string.Empty;
		_editSlotErrors[slotIndex] = string.Empty;
		LogInfo ($"Updated edit slot temperature slot={slotIndex + 1}, day='{EditSelectedDay}', value={parsed.ToString (CultureInfo.InvariantCulture)}");
		EditScheduleError = string.Empty;
		if (!string.IsNullOrEmpty (previousSlotError))
			NotifyPropertyChanged (slotErrorPropertyId, new DriverEntityValue (string.Empty));
		}

	private bool TryBuildEditedDaySchedule (out Dictionary<string, object>? editedDaySchedule, out string errorMessage)
		{
		editedDaySchedule = null;
		errorMessage = string.Empty;

		for (int i = 0; i < MaxEditableScheduleSlots; i++)
			_editSlotErrors[i] = string.Empty;

		if (!TryGetEditableSlotsForSelectedDay (out List<EditableScheduleSlot>? slots) || slots == null)
			{
			errorMessage = "No editable schedule is loaded";
			NotifyEditStateChanged ();
			return false;
			}

		var normalizedSlots = new List<EditableScheduleSlot> (slots.Count);
		for (int i = 0; i < slots.Count; i++)
			{
			EditableScheduleSlot slot = slots[i];
			if (!TryParseTimeText (slot.Time, out string normalizedTime))
				{
				_editSlotErrors[i] = "Use HH:mm";
				errorMessage = $"Invalid time in slot {i + 1}";
				NotifyEditStateChanged ();
				return false;
				}

			normalizedSlots.Add (new EditableScheduleSlot (normalizedTime, Math.Round (slot.Temperature * 2.0, MidpointRounding.AwayFromZero) / 2.0));
			}

		editedDaySchedule = BuildRawDaySchedule (normalizedSlots);
		NotifyEditStateChanged ();
		return true;
		}

	private bool TryGetEditableSlotsForSelectedDay (out List<EditableScheduleSlot>? slots) =>
		_editDaySlots.TryGetValue (EditSelectedDay ?? string.Empty, out slots);

	private bool TryGetEditableSlot (int slotIndex, out EditableScheduleSlot? slot)
		{
		slot = null;
		if (!TryGetEditableSlotsForSelectedDay (out List<EditableScheduleSlot>? slots) || slots == null)
			return false;

		if (slotIndex < 0 || slotIndex >= slots.Count || slotIndex >= MaxEditableScheduleSlots)
			return false;

		slot = slots[slotIndex];
		return true;
		}

	private void SetEditError (string errorText, bool notify)
		{
		EditScheduleError = errorText ?? string.Empty;
		if (notify)
			NotifyEditSlotStateChanged ();
		}

	private string BuildEditScheduleSummary ()
		{
		if (!EditScheduleEnabled)
			return "No schedule assigned";

		if (string.IsNullOrWhiteSpace (EditSelectedDay))
			return _editScheduleName;

		return $"{_editScheduleName} - {EditSelectedDay}";
		}

	private static string BuildEditScheduleImpact (WiserHeatingSchedule schedule)
		{
		if (schedule == null)
			return string.Empty;

		List<string> names = schedule.AssignmentNames ?? [];
		return names.Count == 0
			? "Updates the shared schedule"
			: $"Updates all assigned rooms: {string.Join (", ", names)}";
		}

	private string ResolveInitialEditDay (string preferredDay)
		{
		if (!string.IsNullOrWhiteSpace (preferredDay))
			{
			string matchingPreferredDay = EditableScheduleDays.FirstOrDefault (day => string.Equals (day, preferredDay, StringComparison.OrdinalIgnoreCase));
			if (!string.IsNullOrWhiteSpace (matchingPreferredDay))
				return matchingPreferredDay;
			}

		return EditableScheduleDays[(int) DateTime.Today.DayOfWeek];
		}

	private void ResetEditSlotState ()
		{
		for (int i = 0; i < MaxEditableScheduleSlots; i++)
			{
			_editSlotTimes[i] = string.Empty;
			_editSlotTemperatures[i] = 0;
			_editSlotVisible[i] = false;
			_editSlotErrors[i] = string.Empty;
			}
		}

	private void NotifyEditStateChanged ()
		{
		NotifyPropertyChanged ("editScheduleEnabled", new DriverEntityValue (EditScheduleEnabled));
		NotifyPropertyChanged ("editScheduleSummary", new DriverEntityValue (EditScheduleSummary ?? string.Empty));
		NotifyPropertyChanged ("editScheduleImpact", new DriverEntityValue (EditScheduleImpact ?? string.Empty));
		NotifyPropertyChanged ("editScheduleError", new DriverEntityValue (EditScheduleError ?? string.Empty));
		NotifyPropertyChanged ("editSelectedDay", new DriverEntityValue (EditSelectedDay));
		NotifyEditSlotStateChanged ();
		}

	private void NotifyEditSlotStateChanged ()
		{
		for (int i = 0; i < MaxEditableScheduleSlots; i++)
			{
			NotifyPropertyChanged (GetEditSlotVisiblePropertyId (i), new DriverEntityValue (_editSlotVisible[i]));
			NotifyPropertyChanged (GetEditSlotTimePropertyId (i), new DriverEntityValue (_editSlotTimes[i] ?? string.Empty));
			NotifyPropertyChanged (GetEditSlotErrorPropertyId (i), new DriverEntityValue (_editSlotErrors[i] ?? string.Empty));
			NotifyPropertyChanged (GetEditSlotTemperaturePropertyId (i), new DriverEntityValue (_editSlotTemperatures[i]));
			}
		}

	private static List<EditableScheduleSlot> BuildEditableSlots (object daySchedule)
		{
		var slots = new List<EditableScheduleSlot> ();
		if (daySchedule is not IDictionary<string, object> dayDict)
			return slots;

		if (!TryGetScheduleValueList (dayDict, "Time", out List<object> times) ||
			!TryGetScheduleValueList (dayDict, "DegreesC", out List<object> temperatures))
			return slots;

		for (int i = 0; i < times.Count && i < temperatures.Count && i < MaxEditableScheduleSlots; i++)
			{
			string timeValue = Convert.ToInt32 (times[i], CultureInfo.InvariantCulture).ToString ("D4", CultureInfo.InvariantCulture);
			string formattedTime = DateTime.ParseExact (timeValue, "HHmm", CultureInfo.InvariantCulture).ToString ("HH:mm", CultureInfo.InvariantCulture);
			double temp = WiserTemperatureFunctions.FromWiserTemp (temperatures[i]);
			slots.Add (new EditableScheduleSlot (formattedTime, temp));
			}

		return slots;
		}

	private static bool TryGetScheduleValueList (IDictionary<string, object> dayDict, string key, out List<object> values)
		{
		values = [];
		if (!dayDict.TryGetValue (key, out object value) || value == null)
			return false;

		if (value is List<object> objectList)
			{
			values = objectList;
			return true;
			}

		if (value is IEnumerable<object> enumerable)
			{
			values = enumerable.ToList ();
			return true;
			}

		return false;
		}

	private static Dictionary<string, object> BuildRawDaySchedule (IEnumerable<EditableScheduleSlot> slots)
		{
		List<EditableScheduleSlot> orderedSlots = [.. slots.OrderBy (slot => slot.Time, StringComparer.OrdinalIgnoreCase)];
		return new Dictionary<string, object>
			{
				["Time"] = orderedSlots
					.Select (slot => int.Parse (slot.Time.Replace (":", string.Empty), CultureInfo.InvariantCulture))
					.ToList (),
				["DegreesC"] = orderedSlots.Select (slot => WiserTemperatureFunctions.ToWiserTemp (slot.Temperature)).ToList ()
			};
		}

	private static string DescribeDaySchedule (IDictionary<string, object>? daySchedule)
		{
		if (daySchedule == null)
			return "<null>";

		IEnumerable<object>? times = null;
		if (daySchedule.TryGetValue ("Time", out object timesObj) && timesObj is IEnumerable<object> timeValues)
			times = timeValues;

		IEnumerable<object>? temps = null;
		if (daySchedule.TryGetValue ("DegreesC", out object tempsObj) && tempsObj is IEnumerable<object> tempValues)
			temps = tempValues;

		string timesText = times != null ? string.Join (",", times) : "<missing>";
		string tempsText = temps != null ? string.Join (",", temps) : "<missing>";
		return $"Time=[{timesText}] DegreesC=[{tempsText}]";
		}

	private static IDictionary<string, object> CloneScheduleData (IDictionary<string, object> source) =>
		source?.ToDictionary (kvp => kvp.Key, kvp => CloneScheduleValue (kvp.Value), StringComparer.OrdinalIgnoreCase)
		?? new Dictionary<string, object> (StringComparer.OrdinalIgnoreCase);

	private static object CloneScheduleValue (object value)
		{
		if (value is IDictionary<string, object> dictionary)
			return CloneScheduleData (dictionary);

		if (value is IEnumerable<object> enumerable && value is not string)
			return enumerable.Select (CloneScheduleValue).ToList ();

		return value;
		}

	private static string NormalizeTimeText (string timeText) =>
		string.IsNullOrWhiteSpace (timeText) ? string.Empty : timeText.Trim ();

	private static DriverEntityAvailableValue[] BuildEditTimeAvailableValues ()
		{
		var values = new List<DriverEntityAvailableValue> (48);
		for (int hour = 0; hour < 24; hour++)
			{
				for (int minute = 0; minute < 60; minute += 30)
					{
						string timeText = new DateTime (1, 1, 1, hour, minute, 0).ToString ("HH:mm", CultureInfo.InvariantCulture);
						// Crestron selectorbutton renders selector labels from LocalizationKey instead of Text; keep both equal to the displayed time text.
						values.Add (new DriverEntityAvailableValue (
							timeText,
							new DriverEntityLocalizedString (timeText, timeText),
							false));
					}
			}

		return [.. values];
		}

	private static bool TryParseTimeText (string timeText, out string normalizedTime)
		{
		string candidate = NormalizeTimeText (timeText);
		foreach (string format in new[] { "HH:mm", "H:mm", "HHmm", "Hmm" })
			{
				if (DateTime.TryParseExact (candidate, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
					{
					normalizedTime = parsed.ToString ("HH:mm", CultureInfo.InvariantCulture);
					return true;
					}
			}

		normalizedTime = string.Empty;
		return false;
		}

	private static string GetEditSlotVisiblePropertyId (int slotIndex) => $"editSlot{slotIndex + 1}Visible";
	private static string GetEditSlotTimePropertyId (int slotIndex) => $"editSlot{slotIndex + 1}Time";
	private static string GetEditSlotErrorPropertyId (int slotIndex) => $"editSlot{slotIndex + 1}Error";
	private static string GetEditSlotTemperaturePropertyId (int slotIndex) => $"editSlot{slotIndex + 1}Temperature";

	private DriverEntityAvailableValue[] BuildScheduleAvailableValues ()
		{
		var values = new List<DriverEntityAvailableValue> ();
		var diagnostics = new List<string> ();
		foreach (WiserHeatingSchedule? schedule in _platform.HeatingSchedules ?? [])
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
		foreach (WiserHeatingSchedule? schedule in _platform.HeatingSchedules ?? [])
			diagnostics.Add ($"{schedule?.Id ?? 0}:{BuildScheduleOptionValue (schedule)}");

		WiserHeatingSchedule? assignedSchedule = ResolveAssignedSchedule (_room);
		LogInfo ($"{context}; availableCount={_scheduleValues?.Length ?? 0}, selectedScheduleId='{SelectedScheduleId ?? string.Empty}', roomScheduleId={assignedSchedule?.Id ?? _room.ScheduleId}, roomScheduleName='{ResolveScheduleNameForLog (_room, assignedSchedule)}', platformSchedules={(diagnostics.Count == 0 ? "<none>" : string.Join (", ", diagnostics))}");
		}

	[Conditional ("DEBUG")]
	private void LogInfo (string message) =>
		_logger.Log (_driverLogId, LogEntryLevel.Info, $"Room '{DeviceLabel ?? ControllerId}': {message}");

	private static string ResolveScheduleNameForLog (WiserRoom room, WiserHeatingSchedule? assignedSchedule)
		{
		if (assignedSchedule == null)
			return string.Empty;

		if (!string.IsNullOrWhiteSpace (assignedSchedule.Name))
			return assignedSchedule.Name!;

		return $"Schedule {assignedSchedule.Id}";
		}

	private static string BuildScheduleOptionValue (WiserHeatingSchedule? schedule) =>
		string.IsNullOrWhiteSpace (schedule?.Name)
			? $"Schedule {schedule?.Id ?? 0}"
			: schedule!.Name!;

	private static bool SetValue (string value, ref string field)
		{
		if (string.Equals (field, value, StringComparison.Ordinal))
			return false;

		field = value;
		return true;
		}

	private bool SetAndNotify (string propertyId, string value, ref string field)
		{
		if (string.Equals (field, value, StringComparison.Ordinal))
			return false;

		field = value;
		NotifyPropertyChanged (propertyId, new DriverEntityValue (value));
		return true;
		}

	private bool SetAndNotify (string propertyId, bool value, ref bool field)
		{
		if (field == value)
			return false;

		field = value;
		NotifyPropertyChanged (propertyId, new DriverEntityValue (value));
		return true;
		}

	private bool SetAndNotify (string propertyId, double value, ref double field)
		{
		LogInfo ($"Set property '{propertyId}' value={value} (current={field})");

		if (field.Equals (value))
			return false;

		field = value;
		NotifyPropertyChanged (propertyId, new DriverEntityValue (value));
		return true;
		}

	private bool SetAndNotify (string propertyId, int value, ref int field)
		{
		if (field == value)
			return false;

		field = value;
		NotifyPropertyChanged (propertyId, new DriverEntityValue (value));
		return true;
		}

	private static bool IsRoomBoostActive (WiserRoom room)
		{
		return room.BoostTimeRemaining > 0;
		}

	private sealed class EditableScheduleSlot (string time, double temperature)
		{
		public string Time { get; set; } = time ?? string.Empty;
		public double Temperature { get; set; } = temperature;
		}

	private string BuildTileIcon (WiserRoom room)
		{
		if (room.IsHeating)
			return "icHeatingRegular";

		if (IsScheduleEnabled (room))
			return "icClimateSchedule";

		return "icClimateRegular";
		}

	}
