// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed class SchedulePendingObservation
	{
	private readonly JsonElement _editor;
	private readonly JsonElement _schedules;
	private readonly JsonElement _rooms;

	public SchedulePendingObservation (ScheduleHubSnapshot hub, JsonElement editor, int scheduleId)
		{
		ScheduleEditorObservation.RequireMatchesHub (editor, hub.Schedules, scheduleId);
		_editor = editor.Clone ();
		_schedules = ScheduleEditorObservation.PersistentSchedules (hub.Schedules);
		_rooms = ScheduleEditorObservation.RoomAssignments (hub.Domain);
		}

	public void RequirePreserved (ScheduleHubSnapshot hub, JsonElement editor)
		{
		if (!JsonElement.DeepEquals (_editor, editor))
			throw new InvalidDataException ("Pending edits changed the peer editor; no peer correction will be attempted.");
		if (!JsonElement.DeepEquals (_schedules, ScheduleEditorObservation.PersistentSchedules (hub.Schedules)) ||
			!JsonElement.DeepEquals (_rooms, ScheduleEditorObservation.RoomAssignments (hub.Domain)))
			throw new InvalidDataException ("Persistent hub schedules or guarded room settings changed during unsaved edits.");
		}
	}