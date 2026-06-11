// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds (15);

	private readonly DriverControllerCreationArgs _args;
	private readonly DriverImplementationResources _resources;
	private readonly DriverControllerLogger _logger;
	private readonly string _driverLogId;
	private readonly WiserWorkQueue _workQueue = new ();
	private readonly UiDefinitionProperty _uiDefinition;
	private readonly Dictionary<string, WiserRoomEntity> _roomEntities = new (StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ConfigurableDriverEntity> _roomControllers = new (StringComparer.OrdinalIgnoreCase);
	private readonly object _entitiesLock = new ();

	private WiserAPI _api = null!;
	private string _hubIpAddress = string.Empty;
	private string _hubSecret = string.Empty;
	private string _temperatureUnits = "Celsius";
	private double _boostDelta = 2.0;
	private int _boostDurationMinutes = 60;
	private bool _enableWholeHouseHotWater;
	private bool _allowAwayMode;
	private int _hotWaterCommandInProgress;
	private int _awayModeCommandInProgress;
	private CancellationTokenSource? _refreshLoopCancellationTokenSource;
	private Task? _refreshLoopTask;

	internal DataDrivenConfigurationController ConfigurationController
		{
		get;
		}

	internal string TemperatureUnits => _temperatureUnits;
	internal double BoostDelta => _boostDelta;
	internal int BoostDurationMinutes => _boostDurationMinutes;
	internal bool HasHeatingSchedules => _api.Schedules?.HeatingSchedules?.Count > 0;
	internal IReadOnlyList<WiserHeatingSchedule> HeatingSchedules => _api.Schedules?.HeatingSchedules?
		.OrderBy (schedule => schedule.Name ?? string.Empty)
		.ThenBy (schedule => schedule.Id)
		.ToList () ?? [];

	internal WiserHeatingSchedule? GetAssignedScheduleForRoom (int roomId) =>
		_api.Schedules?.GetByRoomId (roomId);

	internal async Task<bool> SaveHeatingScheduleAsync (int scheduleId, IDictionary<string, object> scheduleData)
		{
		if (scheduleData == null)
			return false;

		WiserHeatingSchedule? schedule = _api.Schedules?.GetById (WiserScheduleType.Heating, scheduleId) as WiserHeatingSchedule;
		if (schedule == null)
			return false;

		Log ($"SaveHeatingScheduleAsync saving scheduleId={scheduleId} name='{schedule.Name ?? string.Empty}'");
		bool succeeded = await schedule.SetScheduleAsync (scheduleData, CancellationToken.None).ConfigureAwait (false);
		if (!succeeded)
			return false;

		await RefreshSystemStateAsync ().ConfigureAwait (false);
		Log ($"SaveHeatingScheduleAsync completed scheduleId={scheduleId}");
		return true;
		}

	[EntityProperty (Id = "platformStatus", FriendlyName = "Platform Status", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string PlatformStatus
		{
		get;
		private set => SetAndNotify ("platformStatus", value, ref field);
		} = "Not configured";

	[EntityProperty (Id = "platformLastError", FriendlyName = "Platform Last Error", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string PlatformLastError
		{
		get;
		private set => SetAndNotify ("platformLastError", value, ref field);
		} = string.Empty;

	[EntityProperty (
		Id = "platform:managedDevices",
		Type = DriverEntityValueType.DeviceDictionary,
		ItemTypeRef = "platform:ManagedDevice",
		FriendlyName = "Managed Rooms")]
	public IDictionary<string, PlatformManagedDevice> ManagedDevices
		{
		get;
		private set => SetAndNotifyManagedDevices (value, ref field);
		} = new Dictionary<string, PlatformManagedDevice> (StringComparer.OrdinalIgnoreCase);

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

	[EntityProperty (Id = "hotWaterVisible", FriendlyName = "Hot Water Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool HotWaterVisible
		{
		get;
		private set => SetAndNotify ("hotWaterVisible", value, ref field);
		}

	[EntityProperty (Id = "hotWaterActionEnabled", FriendlyName = "Hot Water Action Enabled", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool HotWaterActionEnabled
		{
		get;
		private set => SetAndNotify ("hotWaterActionEnabled", value, ref field);
		}

	[EntityProperty (Id = "hotWaterIsOn", FriendlyName = "Hot Water Is On", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool HotWaterIsOn
		{
		get;
		private set => SetAndNotify ("hotWaterIsOn", value, ref field);
		}

	[EntityProperty (Id = "hotWaterStateLabel", FriendlyName = "Hot Water State Label", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string HotWaterStateLabel
		{
		get;
		private set => SetAndNotify ("hotWaterStateLabel", value, ref field);
		} = string.Empty;

	[EntityProperty (Id = "hotWaterActionLabel", FriendlyName = "Hot Water Action Label", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string HotWaterActionLabel
		{
		get;
		private set => SetAndNotify ("hotWaterActionLabel", value, ref field);
		} = string.Empty;

	[EntityProperty (Id = "hotWaterTileIcon", FriendlyName = "Hot Water Tile Icon", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string HotWaterTileIcon
		{
		get;
		private set => SetAndNotify ("hotWaterTileIcon", value, ref field);
		} = "icFireOff";

	[EntityProperty (Id = "platformOptionsVisible", FriendlyName = "Platform Options Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool PlatformOptionsVisible
		{
		get;
		private set => SetAndNotify ("platformOptionsVisible", value, ref field);
		}

	[EntityProperty (Id = "platformTileIcon", FriendlyName = "Platform Tile Icon", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string PlatformTileIcon
		{
		get;
		private set => SetAndNotify ("platformTileIcon", value, ref field);
		} = "icClimateRegular";

	[EntityProperty (Id = "platformTileStatus", FriendlyName = "Platform Tile Status", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string PlatformTileStatus
		{
		get;
		private set => SetAndNotify ("platformTileStatus", value, ref field);
		} = string.Empty;

	[EntityProperty (Id = "awayModeVisible", FriendlyName = "Away Mode Visible", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool AwayModeVisible
		{
		get;
		private set => SetAndNotify ("awayModeVisible", value, ref field);
		}

	[EntityProperty (Id = "awayModeActionEnabled", FriendlyName = "Away Mode Action Enabled", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool AwayModeActionEnabled
		{
		get;
		private set => SetAndNotify ("awayModeActionEnabled", value, ref field);
		}

	[EntityProperty (Id = "awayModeIsEnabled", FriendlyName = "Away Mode Is Enabled", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool AwayModeIsEnabled
		{
		get;
		private set => SetAndNotify ("awayModeIsEnabled", value, ref field);
		}

	[EntityProperty (Id = "awayModeStateLabel", FriendlyName = "Away Mode State Label", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string AwayModeStateLabel
		{
		get;
		private set => SetAndNotify ("awayModeStateLabel", value, ref field);
		} = string.Empty;

	[EntityProperty (Id = "awayModeActionLabel", FriendlyName = "Away Mode Action Label", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string AwayModeActionLabel
		{
		get;
		private set => SetAndNotify ("awayModeActionLabel", value, ref field);
		} = string.Empty;

	public WiserPlatformDriver (
		DriverControllerCreationArgs args,
		DriverImplementationResources resources)
		: base (DriverController.RootControllerId)
		{
		_args = args;
		_resources = resources;
		_logger = args.Logger;
		_driverLogId = args.DriverId;

		try
			{
			_uiDefinition = UiDefinitionProperty.LoadFromDirectoryIfExists (_args.DriverDataDirectoryPath, resources.InitLogger, LogEntryLevel.Error)
				?? throw new InvalidOperationException ($"UiDefinition is required but was not found at '{_args.DriverDataDirectoryPath}'.");
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

		var cfgArgs = DataDrivenConfigurationControllerArgs.FromResources (args, resources, ControllerId);
		ConfigurationController = new DelegateDataDrivenConfigurationController (
			cfgArgs,
			ApplyConfigurationItems,
			null,
			null);
		}

	public override void Dispose ()
		{
		StopRefreshLoop ();
		_workQueue.Stop ();
		DisposeApi ();
		base.Dispose ();
		}

	[Conditional ("DEBUG")]
	private void Log (string message) =>
		_logger?.Log (_driverLogId, LogEntryLevel.Info, message);

	private void LogError (string message) =>
		_logger?.Log (_driverLogId, LogEntryLevel.Error, message);

	private void SetAndNotify (string propertyId, string value, ref string field)
		{
		if (string.Equals (field, value, StringComparison.Ordinal))
			return;

		field = value;
		NotifyPropertyChanged (propertyId, new DriverEntityValue (value));
		}

	private void SetAndNotify (string propertyId, bool value, ref bool field)
		{
		if (field == value)
			return;

		field = value;
		NotifyPropertyChanged (propertyId, new DriverEntityValue (value));
		}

	private void SetAndNotifyManagedDevices (
		IDictionary<string, PlatformManagedDevice> value,
		ref IDictionary<string, PlatformManagedDevice> field)
		{
		value ??= new Dictionary<string, PlatformManagedDevice> (StringComparer.OrdinalIgnoreCase);

		if (ReferenceEquals (field, value))
			return;

		field = value;
		NotifyPropertyChanged ("platform:managedDevices", CreateValueForEntries (field));
		}

	private void DisposeApi ()
		{
		if (_api == null)
			return;

		_api.Dispose ();
		_api = null!;
		}

	private void StartRefreshLoop ()
		{
		StopRefreshLoop ();

		if (_api == null)
			return;

		var cancellationTokenSource = new CancellationTokenSource ();
		_refreshLoopCancellationTokenSource = cancellationTokenSource;
		_refreshLoopTask = Task.Run (() => RunRefreshLoopAsync (cancellationTokenSource.Token));
		}

	private void StopRefreshLoop ()
		{
		CancellationTokenSource? cancellationTokenSource = Interlocked.Exchange (ref _refreshLoopCancellationTokenSource, null);
		if (cancellationTokenSource == null)
			return;

		try
			{
			cancellationTokenSource.Cancel ();
			}
		catch
			{
			}

		cancellationTokenSource.Dispose ();
		_refreshLoopTask = null;
		}

	private async Task RunRefreshLoopAsync (CancellationToken cancellationToken)
		{
		try
			{
			while (!cancellationToken.IsCancellationRequested)
				{
				await Task.Delay (RefreshInterval, cancellationToken).ConfigureAwait (false);

				if (cancellationToken.IsCancellationRequested || _api == null || !OnlineIndicatorIsOnline)
					continue;

				try
					{
					await _workQueue.EnqueueAsync (async api =>
						{
						if (cancellationToken.IsCancellationRequested || !ReferenceEquals (api, _api))
							return;

						await RefreshSystemStateAsync ().ConfigureAwait (false);
						}).ConfigureAwait (false);
					}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
					}
				catch (Exception ex)
					{
					LogError ("Periodic refresh failed: " + ex);
					PlatformLastError = ex.Message;
					}
				}
			}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
			}
		}

	private void UpdateStatus (string status, string? error = null)
		{
		PlatformStatus = status ?? string.Empty;
		if (error != null)
			PlatformLastError = error;
		}

	private void SetOnline (bool online) => OnlineIndicatorIsOnline = online;

	private void SetReady (bool ready) => ReadyIndicatorIsReady = ready;

	[EntityCommand (Id = "toggleHotWater", FriendlyName = "Toggle Hot Water")]
	public void ToggleHotWater () => _ = ToggleHotWaterAsync ();

	[EntityCommand (Id = "toggleAwayMode", FriendlyName = "Toggle Away Mode")]
	public void ToggleAwayMode () => _ = ToggleAwayModeAsync ();

	private ConfigurationItemErrors? ApplyConfigurationItems (
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

			if (values.TryGetValue ("EnableWholeHouseHotWater", out DriverEntityValue? enableWholeHouseHotWater) && enableWholeHouseHotWater.HasValue)
				_enableWholeHouseHotWater = ReadBooleanValue (enableWholeHouseHotWater.Value, _enableWholeHouseHotWater);

			if (values.TryGetValue ("AllowAwayMode", out DriverEntityValue? allowAwayMode) && allowAwayMode.HasValue)
				_allowAwayMode = ReadBooleanValue (allowAwayMode.Value, _allowAwayMode);

			UpdatePlatformOptionsState ();
			}

		ConfigurationItemErrors? ValidateConnectionItems ()
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
				ConfigurationItemErrors? allErrors = ValidateConnectionItems ();
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
					{
					ConfigurationItemErrors? heatSettingsErrors = ValidateConnectionItems ();
					if (heatSettingsErrors != null)
						return heatSettingsErrors;

					_ = ConnectAndDiscoverAsync ();
					return null;
					}

				return null;

			case DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues:
				Log ("ApplyConfigurationItems: action=ClearValues");
				DisposeApi ();
				_workQueue.ClearClient ();
				_enableWholeHouseHotWater = false;
				_allowAwayMode = false;
				SetReady (false);
				SetOnline (false);
				UpdatePlatformOptionsState ();
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

	private static bool ReadBooleanValue (DriverEntityValue value, bool fallback)
		{
		try
			{
			return value.GetValue<bool> ();
			}
		catch
			{
			try
				{
				return value.GetValue<int> () != 0;
				}
			catch
				{
				try
					{
					string text = value.GetValue<string> ();
					if (bool.TryParse (text, out bool parsedBool))
						return parsedBool;

					if (int.TryParse (text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
						return parsedInt != 0;

					return fallback;
					}
				catch
					{
					return fallback;
					}
				}
			}
		}

	private void UpdatePlatformOptionsState ()
		{
		HotWaterVisible = _enableWholeHouseHotWater;
		AwayModeVisible = _allowAwayMode;
		PlatformOptionsVisible = HotWaterVisible || AwayModeVisible;

		if (!_enableWholeHouseHotWater)
			{
			HotWaterActionEnabled = false;
			HotWaterIsOn = false;
			HotWaterStateLabel = string.Empty;
			HotWaterActionLabel = string.Empty;
			HotWaterTileIcon = "icFireOff";
			}
		else
			{
			WiserHotwater? hotwater = _api?.Hotwater;
			if (hotwater == null)
				{
				HotWaterActionEnabled = false;
				HotWaterIsOn = false;
				HotWaterStateLabel = "^HotWaterUnavailableLabel";
				HotWaterActionLabel = "^HotWaterUnavailableActionLabel";
				HotWaterTileIcon = "icFireOff";
				}
			else
				{
				bool isOn = hotwater.IsHeating;
				HotWaterActionEnabled = OnlineIndicatorIsOnline && Interlocked.CompareExchange (ref _hotWaterCommandInProgress, 0, 0) == 0;
				HotWaterIsOn = isOn;
				HotWaterStateLabel = isOn ? "^HotWaterOnLabel" : "^HotWaterOffLabel";
				HotWaterActionLabel = isOn ? "^HotWaterTurnOffLabel" : "^HotWaterTurnOnLabel";
				HotWaterTileIcon = isOn ? "icFireOn" : "icFireOff";
				}
			}

		if (!_allowAwayMode)
			{
			AwayModeActionEnabled = false;
			AwayModeIsEnabled = false;
			AwayModeStateLabel = string.Empty;
			AwayModeActionLabel = string.Empty;
			}
		else
			{
			WiserSystem? system = _api?.System;
			if (system == null)
				{
				AwayModeActionEnabled = false;
				AwayModeIsEnabled = false;
				AwayModeStateLabel = "^AwayUnavailableLabel";
				AwayModeActionLabel = "^AwayUnavailableActionLabel";
				}
			else
				{
				bool isAwayEnabled = system.AwayModeEnabled;
				AwayModeActionEnabled = OnlineIndicatorIsOnline && Interlocked.CompareExchange (ref _awayModeCommandInProgress, 0, 0) == 0;
				AwayModeIsEnabled = isAwayEnabled;
				AwayModeStateLabel = isAwayEnabled ? "^AwayEnabledLabel" : "^AwayDisabledLabel";
				AwayModeActionLabel = isAwayEnabled ? "^AwayDisableActionLabel" : "^AwayEnableActionLabel";
				}
			}

		if (!PlatformOptionsVisible)
			{
			PlatformTileIcon = "icClimateRegular";
			PlatformTileStatus = string.Empty;
			}
		else if (HotWaterVisible)
			{
			PlatformTileIcon = HotWaterTileIcon;
			PlatformTileStatus = HotWaterStateLabel;
			}
		else
			{
			PlatformTileIcon = AwayModeIsEnabled ? "icLeaveHome" : "icClimateRegular";
			PlatformTileStatus = AwayModeStateLabel;
			}
		}

	private async Task ToggleHotWaterAsync ()
		{
		if (!_enableWholeHouseHotWater)
			return;

		if (Interlocked.Exchange (ref _hotWaterCommandInProgress, 1) != 0)
			return;

		UpdatePlatformOptionsState ();

		try
			{
			await _workQueue.EnqueueAsync (async api =>
				{
					WiserHotwater? hotwater = api.Hotwater;
					if (hotwater == null)
						return;

					bool requestedOn = !HotWaterIsOn;
					bool success = await hotwater.OverrideStateAsync (requestedOn ? "On" : "Off", CancellationToken.None).ConfigureAwait (false);
					if (success)
						await RefreshSystemStateAsync ().ConfigureAwait (false);
				}).ConfigureAwait (false);

			UpdatePlatformOptionsState ();
			}
		catch (Exception ex)
			{
			LogError ("Toggle hot water failed: " + ex);
			PlatformLastError = ex.Message;
			UpdatePlatformOptionsState ();
			}
		finally
			{
			Interlocked.Exchange (ref _hotWaterCommandInProgress, 0);
			UpdatePlatformOptionsState ();
			}
		}

	private async Task ToggleAwayModeAsync ()
		{
		if (!_allowAwayMode)
			return;

		if (Interlocked.Exchange (ref _awayModeCommandInProgress, 1) != 0)
			return;

		UpdatePlatformOptionsState ();

		try
			{
			await _workQueue.EnqueueAsync (async api =>
				{
					WiserSystem? system = api.System;
					if (system == null)
						return;

					bool requestedOn = !system.AwayModeEnabled;
					system.AwayModeEnabled = requestedOn;
					await RefreshSystemStateAsync ().ConfigureAwait (false);
				}).ConfigureAwait (false);

			UpdatePlatformOptionsState ();
			}
		catch (Exception ex)
			{
			LogError ("Toggle away mode failed: " + ex);
			PlatformLastError = ex.Message;
			UpdatePlatformOptionsState ();
			}
		finally
			{
			Interlocked.Exchange (ref _awayModeCommandInProgress, 0);
			UpdatePlatformOptionsState ();
			}
		}

	private async Task ConnectAndDiscoverAsync ()
		{
		WiserAPI? newApi = null;
		try
			{
			StopRefreshLoop ();
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

			newApi = new WiserAPI (_hubIpAddress, _hubSecret, units)
				?? throw new InvalidOperationException ("Wiser API client creation returned null.");
			Log ("Wiser API client created; initializing against host=" + _hubIpAddress);
			await newApi.InitializeAsync (CancellationToken.None).ConfigureAwait (false);
			DisposeApi ();
			_api = newApi;
			newApi = null;
			Log ("Connected to Wiser API");
			_workQueue.SetClient (_api);
			SetOnline (true);
			UpdatePlatformOptionsState ();
			UpdateStatus ("Connected", string.Empty);
			await RefreshSystemStateAsync ().ConfigureAwait (false);
			StartRefreshLoop ();
			}
		catch (Exception ex)
			{
			StopRefreshLoop ();
			newApi?.Dispose ();
			DisposeApi ();
			_workQueue.ClearClient ();
			LogError ("Wiser connection failed: " + ex);
			SetOnline (false);
			SetReady (false);
			UpdatePlatformOptionsState ();
			UpdateStatus ("Offline", ex.Message);
			}
		}

	internal async Task RefreshSystemStateAsync ()
		{
		Log ("RefreshSystemStateAsync: reading hub data");
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
			Log ("RefreshSystemStateAsync - UpdateSubControllers start count=" + controllersToAdd.Count);
			UpdateSubControllers (controllersToAdd, null);
			Log ("RefreshSystemStateAsync - UpdateSubControllers complete count=" + controllersToAdd.Count);
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
		UpdatePlatformOptionsState ();
		Log ("RefreshSystemStateAsync - publishing platform:managedDevices count=" + ManagedDevices.Count);

		if (ManagedDevices.Count == 0)
			UpdateStatus ("Connected - no rooms discovered", string.Empty);
		else
			UpdateStatus ("Connected - rooms discovered: " + ManagedDevices.Count, string.Empty);

		Log ("Discovery complete");
		}

	internal async Task<bool> AdjustRoomSetpointAsync (int roomId, double delta)
		{
		WiserRoom? room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		double newSetpoint = Math.Round (room.CurrentTargetTemperature + delta, 1, MidpointRounding.AwayFromZero);
		await room.SetTargetTemperatureAsync (newSetpoint, CancellationToken.None).ConfigureAwait (false);
		await RefreshSystemStateAsync ().ConfigureAwait (false);
		return true;
		}

	internal async Task<bool> SetRoomSetpointAsync (int roomId, double setpoint)
		{
		WiserRoom? room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		if (room.IsBoost)
			await room.CancelBoostAsync (CancellationToken.None).ConfigureAwait (false);

		if (_api.Schedules?.GetByRoomId (roomId) != null && !string.Equals (room.Mode, "Manual", StringComparison.OrdinalIgnoreCase))
			await room.SetTargetTemperatureForDurationOfScheduleAsync (setpoint, CancellationToken.None).ConfigureAwait (false);
		else
			await room.SetManualTemperatureAsync (setpoint, CancellationToken.None).ConfigureAwait (false);

		await RefreshSystemStateAsync ().ConfigureAwait (false);
		return true;
		}

	internal async Task<bool> TriggerRoomBoostAsync (int roomId)
		{
		WiserRoom? room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		if (room.IsBoost)
			await room.CancelBoostAsync (CancellationToken.None).ConfigureAwait (false);
		else
			await room.BoostAsync (_boostDelta, _boostDurationMinutes, CancellationToken.None).ConfigureAwait (false);

		await RefreshSystemStateAsync ().ConfigureAwait (false);
		return true;
		}

	internal async Task<bool> AdvanceRoomScheduleAsync (int roomId)
		{
		WiserRoom? room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		await room.ScheduleAdvanceAsync (CancellationToken.None).ConfigureAwait (false);
		await RefreshSystemStateAsync ().ConfigureAwait (false);
		return true;
		}

	internal async Task<bool> SetRoomAssignedScheduleAsync (int roomId, int scheduleId)
		{
		WiserRoom? room = _api.Rooms?.GetById (roomId);
		List<WiserHeatingSchedule> schedules = HeatingSchedules.ToList ();
		Log ($"SetRoomAssignedScheduleAsync requested roomId={roomId}, scheduleId={scheduleId}, roomFound={room != null}, scheduleCount={schedules.Count}");

		if (room == null || schedules.Count == 0)
			return false;

		WiserHeatingSchedule? targetSchedule = schedules.FirstOrDefault (schedule => schedule.Id == scheduleId);
		if (targetSchedule == null)
			{
			Log ($"SetRoomAssignedScheduleAsync could not find scheduleId={scheduleId} for roomId={roomId}");
			return false;
			}

		Log ($"SetRoomAssignedScheduleAsync assigning roomId={roomId} to scheduleId={scheduleId} name='{targetSchedule.Name ?? string.Empty}'");

		await targetSchedule.AssignScheduleAsync ([roomId], true, CancellationToken.None).ConfigureAwait (false);
		await RefreshSystemStateAsync ().ConfigureAwait (false);
		Log ($"SetRoomAssignedScheduleAsync completed roomId={roomId}, scheduleId={scheduleId}");
		return true;
		}

	internal async Task<bool> SetRoomScheduleEnabledAsync (int roomId, bool enabled)
		{
		WiserRoom? room = _api.Rooms?.GetById (roomId);
		if (room == null)
			return false;

		if (enabled)
			{
			if (room.Schedule == null)
				{
				List<WiserHeatingSchedule> schedules = HeatingSchedules.ToList ();

				if (schedules.Count == 0)
					return false;

				await schedules[0].AssignScheduleAsync ([roomId], true, CancellationToken.None).ConfigureAwait (false);
				}

			string? autoMode = FindPreferredRoomMode (room, "Auto", "Scheduled", "Schedule");
			if (!string.IsNullOrWhiteSpace (autoMode))
				await room.SetModeAsync (autoMode!, CancellationToken.None).ConfigureAwait (false);
			}
		else
			{
			string? manualMode = FindPreferredRoomMode (room, "Manual", "Fixed");
			if (!string.IsNullOrWhiteSpace (manualMode))
				await room.SetModeAsync (manualMode!, CancellationToken.None).ConfigureAwait (false);
			else
				await room.SetManualTemperatureAsync (room.CurrentTargetTemperature, CancellationToken.None).ConfigureAwait (false);
			}

		await RefreshSystemStateAsync ().ConfigureAwait (false);
		return true;
		}

	private static string? FindPreferredRoomMode (WiserRoom room, params string[] preferredModes)
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