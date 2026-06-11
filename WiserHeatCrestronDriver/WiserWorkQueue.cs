// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using WiserHeatApiV2;

namespace WiserHeat.CrestronDriver;

internal sealed class WiserWorkQueue
	{
	private readonly SemaphoreSlim _gate = new (1, 1);
	private volatile WiserAPI _api = null!;
	private volatile bool _stopped;

	public void SetClient (WiserAPI api)
		{
		if (api == null)
			throw new ArgumentNullException (nameof (api));

		_api = api;
		}

	public void ClearClient ()
		{
		_api = null!;
		}

	public void Stop ()
		{
		_stopped = true;
		_api = null!;
		}

	public async Task EnqueueAsync (Func<WiserAPI, Task> work)
		{
		if (_stopped)
			return;

		if (work == null)
			throw new ArgumentNullException (nameof (work));

		await _gate.WaitAsync ().ConfigureAwait (false);
		try
			{
			if (_stopped)
				return;

			WiserAPI api = _api ?? throw new InvalidOperationException ("Wiser API client is required.");

			await work (api).ConfigureAwait (false);
			}
		finally
			{
			_ = _gate.Release ();
			}
		}
	}