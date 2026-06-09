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
			args, typeof (EntryPoint));
		var platform = new WiserPlatformDriver (args, resources);
		var rootEntity = new ConfigurableDriverEntity (
			platform.ControllerId,
			platform,
			platform.ConfigurationController);

		return new DispatchingDeviceController (rootEntity, args, null);
		}
	}
