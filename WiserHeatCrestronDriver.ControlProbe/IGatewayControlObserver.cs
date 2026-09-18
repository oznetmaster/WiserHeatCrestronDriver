// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

/// <summary>Independent observation. No observer call is a prerequisite for physical compensation.</summary>
public interface IGatewayControlObserver
	{
	Task BeforeInputAsync (ScheduleHubSnapshot hub, CancellationToken token);
	Task AfterInputAsync (ScheduleHubSnapshot hub, CancellationToken token);
	Task AfterRestorationAsync (ScheduleHubSnapshot hub, CancellationToken token);
	}