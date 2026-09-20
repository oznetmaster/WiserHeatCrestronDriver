// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System.Globalization;

using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

using WiserHeatApiV2;

namespace WiserHeat.CrestronDriver;

internal sealed partial class WiserRoomEntity
	{
	[EntityProperty (Id = "isHeatingOff", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool IsHeatingOff => TargetTemperature == Constants.TEMP_OFF;

	[EntityProperty (Id = "hasHeatingTarget", Type = DriverEntityValueType.Boolean)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public bool HasHeatingTarget => !IsHeatingOff;

	[EntityProperty (Id = "minimumHeatingLabel", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string MinimumHeatingLabel => "Set to " + TemperatureDisplay.FromCelsius (TemperatureDisplay.MinimumCelsius, TemperatureUnits).ToString ("0.#", CultureInfo.InvariantCulture) + (TemperatureDisplay.IsFahrenheit (TemperatureUnits) ? "°F" : "°C");

	[EntityCommand (Id = "resumeHeating", FriendlyName = "Resume Heating at Minimum Temperature")]
	public void ResumeHeating ()
		{
		if (IsHeatingOff)
			SetTargetTemperature (TemperatureDisplay.FromCelsius (TemperatureDisplay.MinimumCelsius, TemperatureUnits));
		}

	private void AddOffSlotProperties ()
		{
		for (int index = 0; index < MAX_EDITABLE_SCHEDULE_SLOTS; index++)
			{
			int slot = index;
			foreach (bool off in new[] { true, false })
				{
				bool showOff = off;
				AddProperty (this, OffSlotPropertyId (slot, showOff), new DelegatePropertyInstance (
					DriverEntityPropertyDefinition.Create (DriverEntityValueType.Boolean),
					new DriverEntityPropertyMetadata (programmable: false, extensionUiProperty: true),
					(instance, _) => new DriverEntityValue ((((WiserRoomEntity)instance)._editSlotTemperatures[slot] == Constants.TEMP_OFF) == showOff)));
				}
			}
		}

	private static string OffSlotPropertyId (int slot, bool off) => "editSlot" + (slot + 1).ToString (CultureInfo.InvariantCulture) + (off ? "IsOff" : "HasTemperature");

	private void NotifyOffSlotState (int slot)
		{
		bool off = _editSlotTemperatures[slot] == Constants.TEMP_OFF;
		NotifyPropertyChanged (OffSlotPropertyId (slot, true), new DriverEntityValue (off));
		NotifyPropertyChanged (OffSlotPropertyId (slot, false), new DriverEntityValue (!off));
		}

	private void ResumeEditSlot (int slot)
		{
		if (TryGetEditableSlot (slot, out var pending) && pending != null && pending.Temperature == Constants.TEMP_OFF)
			SetEditSlotTemperatureProperty (slot, GetEditSlotTemperaturePropertyId (slot), TemperatureDisplay.FromCelsius (TemperatureDisplay.MinimumCelsius, TemperatureUnits));
		}

	[EntityCommand (Id = "resumeEditSlot1", FriendlyName = "Set Slot 1 to Minimum Temperature")]
	public void ResumeEditSlot1 () => ResumeEditSlot (0);

	[EntityCommand (Id = "resumeEditSlot2", FriendlyName = "Set Slot 2 to Minimum Temperature")]
	public void ResumeEditSlot2 () => ResumeEditSlot (1);

	[EntityCommand (Id = "resumeEditSlot3", FriendlyName = "Set Slot 3 to Minimum Temperature")]
	public void ResumeEditSlot3 () => ResumeEditSlot (2);

	[EntityCommand (Id = "resumeEditSlot4", FriendlyName = "Set Slot 4 to Minimum Temperature")]
	public void ResumeEditSlot4 () => ResumeEditSlot (3);

	[EntityCommand (Id = "resumeEditSlot5", FriendlyName = "Set Slot 5 to Minimum Temperature")]
	public void ResumeEditSlot5 () => ResumeEditSlot (4);

	[EntityCommand (Id = "resumeEditSlot6", FriendlyName = "Set Slot 6 to Minimum Temperature")]
	public void ResumeEditSlot6 () => ResumeEditSlot (5);

	[EntityCommand (Id = "resumeEditSlot7", FriendlyName = "Set Slot 7 to Minimum Temperature")]
	public void ResumeEditSlot7 () => ResumeEditSlot (6);

	[EntityCommand (Id = "resumeEditSlot8", FriendlyName = "Set Slot 8 to Minimum Temperature")]
	public void ResumeEditSlot8 () => ResumeEditSlot (7);
	}