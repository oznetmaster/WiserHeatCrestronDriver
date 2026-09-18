// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

using WiserHeat.CrestronDriver;

[assembly: DriverAssemblyEntryPoint (typeof (EntryPoint))]

public sealed class EntryPoint : DriverAssemblyEntryPoint
	{
	public override DriverController CreateDriverControllerInstance (
		DriverControllerCreationArgs args)
		{
		var resources = DriverImplementationResources.FromCreationArgs (
			args, typeof (EntryPoint), "WiserHeat.CrestronDriver.Thermostat_WiserHeat_IP_V2.json");
		return CreateController (new WiserPlatformDriver (args, resources), args);
		}

	internal static DriverController CreateController (WiserPlatformDriver platform, DriverControllerCreationArgs args)
		{
		var rootEntity = new ConfigurableDriverEntity (
			platform.ControllerId,
			platform,
			platform.ConfigurationController);

		// Reads must not remove/re-add managed controllers while Home is registering
		// their wrappers. Registration follows actual discovery changes only.
		return new DispatchingDeviceController (rootEntity, args, null);
		}

	}