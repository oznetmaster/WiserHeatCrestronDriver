// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;
using System;
using System.Collections.Generic;

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

		return new RoomCallbackDispatchingDeviceController (rootEntity, args, null, platform);
		}

	// Late-added Wiser rooms can miss the final Crestron Home wrapper promotion step
	// until the child is re-registered. Watch the first real room callbacks so the
	// platform can perform a one-time child rebind after commission completes.
	private sealed class RoomCallbackDispatchingDeviceController : DispatchingDeviceController
		{
		private readonly WiserPlatformDriver _platform;

		public RoomCallbackDispatchingDeviceController (
			ConfigurableDriverEntity rootEntity,
			DriverControllerCreationArgs args,
			IDriverControllerLocalization? localization,
			WiserPlatformDriver platform)
			: base (rootEntity, args, localization)
			{
			_platform = platform;
			}

		public override DriverEntityState GetState (string controllerId)
			{
			LogDispatchCallback (nameof (GetState), controllerId);
			_platform.OnRoomControllerCallback (controllerId);
			return base.GetState (controllerId);
			}

		public override IDictionary<string, string> GetLanguageTranslations (string controllerId)
			{
			LogDispatchCallback (nameof (GetLanguageTranslations), controllerId);
			_platform.OnRoomControllerCallback (controllerId);
			return base.GetLanguageTranslations (controllerId);
			}

		[System.Diagnostics.Conditional ("DEBUG")]
		private void LogDispatchCallback (string operation, string controllerId)
			{
			if (!string.Equals (controllerId, "$root", StringComparison.OrdinalIgnoreCase) &&
				!controllerId.StartsWith ("room_", StringComparison.OrdinalIgnoreCase))
				return;

			_platform.LogDispatchCallback ($"Dispatch callback {operation}; controllerId={controllerId ?? "<null>"}");
			}

		}
	}