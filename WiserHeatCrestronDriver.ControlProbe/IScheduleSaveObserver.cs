// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

public interface IScheduleSaveObserver
	{
	Task BeforeChangesAsync (ScheduleHubSnapshot state, int roomId, CancellationToken token);
	Task BeforeSaveAsync (ScheduleHubSnapshot state, ScheduleSaveCase operation, CancellationToken token);
	Task AfterSaveAsync (ScheduleHubSnapshot state, ScheduleSaveCase operation, CancellationToken token);
	Task AfterRestorationAsync (ScheduleHubSnapshot state, int roomId, CancellationToken token);
	}