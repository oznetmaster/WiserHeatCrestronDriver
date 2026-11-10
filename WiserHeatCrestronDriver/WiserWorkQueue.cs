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

	public Task EnqueueAsync (Func<WiserAPI, Task> work) => EnqueueAsync (_api, work);

	private async Task EnqueueAsync (WiserAPI expected, Func<WiserAPI, Task> work)
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

			WiserAPI api = _api;
			if (api == null || !ReferenceEquals (api, expected))
				return;

			await work (api).ConfigureAwait (false);
			}
		finally
			{
			_ = _gate.Release ();
			}
		}

	public async Task<bool> ExecuteAsync (Func<WiserAPI, Task<bool>> work)
		{
		bool result = false;
		await EnqueueAsync (async api => { result = await work (api).ConfigureAwait (false); }).ConfigureAwait (false);
		return result;
		}

	public async Task DrainAsync ()
		{
		await _gate.WaitAsync ().ConfigureAwait (false);
		_gate.Release ();
		}
	}