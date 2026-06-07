using System;
using System.Threading;
using System.Threading.Tasks;

using WiserHeatApiV2;

namespace WiserHeat.CrestronDriver;

internal sealed class WiserWorkQueue
	{
	private readonly SemaphoreSlim _gate = new (1, 1);
	private volatile WiserAPI _api;
	private volatile bool _stopped;

	public void SetClient (WiserAPI api) => _api = api;

	public void Stop ()
		{
		_stopped = true;
		_api = null;
		}

	public async Task EnqueueAsync (Func<WiserAPI, Task> work)
		{
		if (_stopped || work == null)
			return;

		await _gate.WaitAsync ().ConfigureAwait (false);
		try
			{
			WiserAPI api = _api;
			if (api == null || _stopped)
				return;

			await work (api).ConfigureAwait (false);
			}
		finally
			{
			_ = _gate.Release ();
			}
		}
	}
