// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;

namespace WiserHeatCrestronDriver.Tests;

// The processor test host deliberately removes dependency driver manifests so
// ManifestUtil sees only the host's identity. Exercise embedded definition lookup
// in this desktop SDK project and in the actual-package startup smoke test.
[TestFixture, Category ("Processor")]
public sealed class EntryPointDefinitionTests
	{
	[Test]
	public void EntryPoint_LoadsEmbeddedDefinitionAndCreatesController ()
		{
		using var logger = new DriverLogger ("entrypoint-resource-test");
		var args = new DriverControllerCreationArgs ("entrypoint-resource-test", TestSupport.DataDirectory, logger.AppLogger, null);
		using var controller = new EntryPoint ().CreateDriverControllerInstance (args);
		Assert.That (controller, Is.Not.Null);
		}
	}