using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

namespace WiserHeat.CrestronDriver;

[EntityDataType (Id = "platform:ManagedDevice")]
public class PlatformManagedDevice (
	DeviceUxCategory uxCategory,
	string name,
	string manufacturer,
	string model,
	string serialNumber)
	{
	[EntityProperty]
	public DeviceUxCategory UxCategory
		{
		get; private set;
		} = uxCategory;

	[EntityProperty]
	public string Name
		{
		get; private set;
		} = name;

	[EntityProperty]
	public string Manufacturer
		{
		get; private set;
		} = manufacturer;

	[EntityProperty]
	public string Model
		{
		get; private set;
		} = model;

	[EntityProperty]
	public string SerialNumber
		{
		get; private set;
		} = serialNumber;
	}