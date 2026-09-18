// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private async Task ExerciseEditorBoundaryRowAsync (string check, string[] titles, RoomBinding binding, int slot,
		JsonElement originalEditor, Action<JsonElement, JsonElement> ownTransition, Func<string, object, Task> record, CancellationToken token)
		{
		string property = "editSlot" + slot.ToString (CultureInfo.InvariantCulture) + "Temperature";
		string phase = "boundary-slot-" + slot.ToString (CultureInfo.InvariantCulture);
		await using var client = await OpenEditorObservationConnectionAsync (token);
		JsonElement current = originalEditor;
		JsonElement WithTemperature (decimal value)
			{
			var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>> (originalEditor.GetRawText ())!;
			properties[property] = JsonSerializer.SerializeToElement (value);
			return JsonSerializer.SerializeToElement (properties);
			}
		void Guard (AndroidHierarchy hierarchy, JsonElement expected)
			{
			AndroidWorkflowSession.VerifyContext (_session!.Context);
			var front = CrestronHomeExtensionPages.RequirePage (hierarchy, titles);
			if (!ScheduleEditorRendering.RequireValues (front.MaskedXml, expected, requireComplete: false)
				.Any (value => value.Slot == slot && value.Kind == "Temperature"))
				throw new InvalidDataException ("The selected boundary row is not completely visible on the front editor page.");
			}
		async Task Observe (JsonElement expected, string label)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (60));
			while (true)
				{
				var observed = ScheduleEditorObservation.Editor ((await ReadRoomAsync (client, binding, deadline.Token)).PropertyValues);
				if (JsonElement.DeepEquals (observed, expected)) break;
				if (!JsonElement.DeepEquals (observed, current)) throw new InvalidDataException ("A boundary action changed unrelated editor state.");
				await Task.Delay (250, deadline.Token);
				}
			// Read-only hierarchy observations may wait for rendering; inputs are never repeated.
			while (true)
				{
				var hierarchy = await _session!.Device.CaptureAsync (deadline.Token);
				try { Guard (hierarchy, expected); break; }
				catch (InvalidDataException) { await Task.Delay (250, deadline.Token); }
				}
			await _session!.CaptureAsync (check + "." + label, hierarchy => Guard (hierarchy, expected), deadline.Token);
			current = expected;
			ownTransition (current, current);
			await record (label + "-observed", current);
			}
		async Task Prepare (decimal temperature, string label)
			{
			var before = ScheduleEditorObservation.Editor ((await ReadRoomAsync (client, binding, token)).PropertyValues);
			if (!JsonElement.DeepEquals (before, current)) throw new InvalidDataException ("Unexpected editor state before boundary preparation.");
			var expected = WithTemperature (temperature);
			ownTransition (current, expected);
			await record (label + "-intent", new { Method = "SDK pending-editor setup, not Android input or a hub save", Slot = slot, Before = current, Expected = expected });
			// Use the same SDK property-write route as the Android raise/lower control.
			var parameters = new { property, value = temperature.ToString ("0.0", CultureInfo.InvariantCulture) };
			var response = await client.ExecuteDeviceCommandAsync (binding.DeviceId, "extension:setPropertyValue", parameters, token);
			await record (label + "-response", new { Parameters = parameters, Response = response });
			await Observe (expected, label);
			}
		async Task Tap (bool plus, decimal temperature, string label)
			{
			var expected = WithTemperature (temperature);
			var selector = CrestronHomePages.Resource (plus ? "customdeviceraiselowerwithtext_plus" : "customdeviceraiselowerwithtext_minus")
				with { SiblingText = "SETPOINT " + slot.ToString (CultureInfo.InvariantCulture) };
			ownTransition (current, expected);
			await record (label + "-intent", new { Method = "One Android tap", Slot = slot, Action = plus ? "Plus" : "Minus", Before = current, Expected = expected });
			await _session!.Device.TapAsync (selector, hierarchy =>
				{
				Guard (hierarchy, current);
				var front = CrestronHomeExtensionPages.RequirePage (hierarchy, titles);
				if (front.RequireUnique (selector) != hierarchy.RequireUnique (selector) || !front.RequireUnique (selector).Enabled)
					throw new InvalidDataException ("The boundary action is not uniquely enabled on the front editor page.");
				}, token);
			await Observe (expected, label);
			}
		foreach (decimal limit in new[] { 5m, 30m })
			{
			string label = phase + "-" + limit.ToString (CultureInfo.InvariantCulture);
			await Prepare (limit, label + "-prepare");
			bool outwardPlus = limit == 30m;
			var outward = CrestronHomePages.Resource (outwardPlus ? "customdeviceraiselowerwithtext_plus" : "customdeviceraiselowerwithtext_minus")
				with { SiblingText = "SETPOINT " + slot.ToString (CultureInfo.InvariantCulture) };
			bool enabled = false;
			await _session!.CaptureAsync (check + "." + label + "-limit", hierarchy =>
				{
				Guard (hierarchy, current);
				enabled = CrestronHomeExtensionPages.RequirePage (hierarchy, titles).RequireUnique (outward).Enabled;
				}, token);
			await record (label + "-outward-state", new { Slot = slot, Limit = limit, Enabled = enabled });
			if (enabled)
				{
				await Tap (outwardPlus, limit, label + "-outward");
				// An unchanged immediate snapshot alone cannot establish rejection of a queued command.
				await Task.Delay (1000, token);
				if (!JsonElement.DeepEquals (current, ScheduleEditorObservation.Editor ((await ReadRoomAsync (client, binding, token)).PropertyValues)))
					throw new InvalidDataException ("An outward action escaped the editor limit.");
				}
			decimal inward = limit + (outwardPlus ? -0.5m : 0.5m);
			await Tap (!outwardPlus, inward, label + "-inward");
			await Tap (outwardPlus, limit, label + "-return");
			}
		await Prepare (originalEditor.GetProperty (property).GetDecimal (), phase + "-restore-pending");
		await record (phase + "-coverage", new { Slot = slot, Lower = 5m, Upper = 30m, BothDirections = true, PendingValueRestored = true, HubSaveSent = false });
		}
	}