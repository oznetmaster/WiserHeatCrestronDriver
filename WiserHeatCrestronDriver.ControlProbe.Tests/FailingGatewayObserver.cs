// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

internal sealed class FailingGatewayObserver (string? failAt = null, bool cancel = false, int failOccurrence = 1, bool persistent = false) : IGatewayControlObserver
	{
	public List<string> Calls { get; } = [];
	public Action? BeforeAction { get; init; }
	private bool _failed;
	private Task Observe (string phase, CancellationToken token)
		{
		token.ThrowIfCancellationRequested ();
		Calls.Add (phase);
		if (phase == "before") BeforeAction?.Invoke ();
		if (phase == failAt && Calls.Count (call => call == phase) == failOccurrence || persistent && _failed)
			{
			_failed = true;
			throw cancel ? new OperationCanceledException ("Peer observation cancelled") : new IOException ("Peer unavailable");
			}
		return Task.CompletedTask;
		}
	public Task BeforeInputAsync (ScheduleHubSnapshot hub, CancellationToken token) => Observe ("before", token);
	public Task AfterInputAsync (ScheduleHubSnapshot hub, CancellationToken token) => Observe ("after", token);
	public Task AfterRestorationAsync (ScheduleHubSnapshot hub, CancellationToken token) => Observe ("restored", token);
	}