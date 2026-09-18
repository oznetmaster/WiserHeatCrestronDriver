// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;

using WiserHeatCrestronDriver.ConfigurationProbe;
using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool ObservePeerDuringGatewayControls { get; init; }
		}
	private sealed class GatewayPeerControlObserver (GatewayUiTests fixture, HubSettings hub, string check) : IGatewayControlObserver, IAsyncDisposable
		{
		private PeerObservationSettings _peer = null!;
		private ConfigurationClient? _client;
		private ProcessorOperationLease? _lease;
		private FileStream? _package;
		private GatewayInstanceObservation _first = null!, _second = null!, _lastFirst = null!, _lastSecond = null!;
		private readonly string _binding = HashPeerBinding (hub.HubHost, hub.Secret);
		private readonly string _owner = Guid.NewGuid ().ToString ("N");
		private readonly Stopwatch _inputElapsed = new ();
		private DateTimeOffset _after;
		private int _sequence;
		private bool _completed;
		private Task Record (string phase, object value) => fixture.RecordPeerAsync (check + "." + phase, value);

		public static async Task<GatewayPeerControlObserver?> OpenAsync (GatewayUiTests fixture, HubSettings hub, string check, CancellationToken token)
			{
			if (!fixture._settings!.ObservePeerDuringGatewayControls) return null;
			var observer = new GatewayPeerControlObserver (fixture, hub, check);
			try { await observer.InitializeAsync (token); return observer; }
			catch
				{
				// No control cycle can have started when this factory fails.
				try
					{
					if (observer._lease != null)
						{
						using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (30));
						await observer._lease.ReleaseAsync (cleanup.Token);
						await observer.Record ("preflight-reservation-released", new { Owner = observer._owner });
						}
					}
				catch { /* Preserve the preflight failure; an uncertain lease remains for reconciliation. */ }
				try { await observer.DisposeAsync (); } catch { /* Preserve the original preflight failure. */ }
				throw;
				}
			}
		private async Task InitializeAsync (CancellationToken token)
			{
			_peer = fixture.LoadPeerSettings (hub);
			var context = fixture._session!.Context;
			_package = new FileStream (_peer.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (!Convert.ToHexString (SHA256.HashData (_package)).Equals (context.PackageSha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Peer candidate bytes differ from the active workflow.");
			var package = DriverDeployment.Inspect (_peer.PackagePath);
			if (package.DriverId != context.DriverGuid || package.Version != context.DriverVersion || package.Model != ConfigurationCompatibility.Model)
				throw new InvalidDataException ("Peer candidate identity differs from the active workflow.");
			await Record ("reservation-intent", new { context.RunId, Owner = _owner, _peer.Host, _peer.DeviceId });
			var credential = new NetworkCredential (_peer.UserName, _peer.Password);
			_lease = await ProcessorOperationLease.AcquireAsync (_peer.Host, credential, _peer.SshFingerprint, _owner, token);
			_client = await ConfigurationClient.ConnectAsync (new () { Host = _peer.Host, CertificateSha256 = _peer.CertificateSha256 }, credential, token);
			var catalogue = await _client.GetDriverAsync (_peer.CatalogueId, token);
			if (catalogue == null || catalogue.Id != _peer.CatalogueId || catalogue.Model != package.Model || catalogue.Manufacturer != package.Manufacturer ||
				catalogue.Developer != "Neil Colvin" || !Version.TryParse (catalogue.Version, out var version) || version != Version.Parse (context.DriverVersion))
				throw new InvalidDataException ("Peer catalogue differs from the selected candidate.");
			await VerifyPayloadAsync ("payload-before", token);
			_first = _lastFirst = await ReadFirst (token);
			_second = _lastSecond = await ReadSecond (token);
			GatewayPairObservation.RequirePair (_first, _second);
			if (_second.Name != _peer.Name || _second.LocationId != _peer.LocationId)
				throw new InvalidDataException ("Peer instance differs from the private binding.");
			await Record ("original", new { First = _first, Second = _second });
			}
		private Task<GatewayInstanceObservation> ReadFirst (CancellationToken token) => fixture.ReadInstanceAsync (fixture._processor!,
			fixture._session!.Context.InstalledDriverId, fixture._settings!.CertificateSha256, _binding, token);
		private Task<GatewayInstanceObservation> ReadSecond (CancellationToken token) => fixture.ReadInstanceAsync (_client!, _peer.DeviceId, _peer.CertificateSha256, _binding, token);
		private async Task VerifyPayloadAsync (string phase, CancellationToken token) => await Record (phase,
			await DriverPayloadInspection.CompareAsync (_peer.Host, new NetworkCredential (_peer.UserName, _peer.Password), _peer.SshFingerprint,
				_peer.PackagePath, fixture._session!.Context.PackageSha256, _peer.CatalogueId, TimeSpan.FromMinutes (2), token));

		public async Task BeforeInputAsync (ScheduleHubSnapshot state, CancellationToken token)
			{
			await ObserveAsync (state, "before", fresh: false, token);
			_after = _lastSecond.RefreshUtc;
			_inputElapsed.Restart ();
			}
		public Task AfterInputAsync (ScheduleHubSnapshot state, CancellationToken token) => ObserveAsync (state, "after", fresh: true, token);
		public async Task AfterRestorationAsync (ScheduleHubSnapshot state, CancellationToken token)
			{
			_after = _lastSecond.RefreshUtc;
			await ObserveAsync (state, "restored", fresh: true, token);
			}
		private async Task ObserveAsync (ScheduleHubSnapshot state, string phase, bool fresh, CancellationToken token)
			{
			bool away = state.Domain.GetProperty ("System").GetProperty ("OverrideType").GetString () switch
				{ "None" => false, "Away" => true, _ => throw new InvalidDataException ("Unsupported Away feedback.") };
			bool water = state.Domain.GetProperty ("HotWater").EnumerateArray ().Single ().GetProperty ("WaterHeatingState").GetString () switch
				{ "On" => true, "Off" => false, _ => throw new InvalidDataException ("Unknown hot-water feedback.") };
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (30));
			int observation = ++_sequence;
			for (int attempt = 1; ; attempt++)
				{
				AndroidWorkflowSession.VerifyContext (fixture._session!.Context);
				var first = await ReadFirst (deadline.Token);
				var second = await ReadSecond (deadline.Token);
				GatewayPairObservation.RequirePair (first, second);
				GatewayPairObservation.RequirePreserved (_lastFirst, first);
				GatewayPairObservation.RequirePreserved (_lastSecond, second);
				_lastFirst = first;
				_lastSecond = second;
				bool matches = (!fresh || second.RefreshUtc > _after) && first.Away == away && second.Away == away && first.HotWater == water && second.HotWater == water;
				await Record ($"{observation}.{phase}.{attempt}", new { First = first, Second = second, Hub = state, Matches = matches,
					SecondsSincePreInputObservation = _inputElapsed.Elapsed.TotalSeconds });
				if (matches) return;
				await Task.Delay (TimeSpan.FromSeconds (1), deadline.Token);
				}
			}
		public async Task CompleteAsync (bool physicalRestorationConfirmed, bool preserveFailure)
			{
			if (_completed) throw new InvalidOperationException ("Peer completion cannot be replayed.");
			_completed = true;
			try
				{
				if (!physicalRestorationConfirmed) throw new InvalidDataException ("Shared hub restoration is unconfirmed; retain both processor reservations.");
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (3));
				await _lease!.VerifyAfterReconnectAsync (_peer.Host, cleanup.Token);
				await VerifyPayloadAsync ("payload-after", cleanup.Token);
				var first = await ReadFirst (cleanup.Token);
				var second = await ReadSecond (cleanup.Token);
				GatewayPairObservation.RequirePreserved (_first, first);
				GatewayPairObservation.RequirePreserved (_second, second);
				await Record ("configuration-preserved", new { First = first, Second = second });
				await _lease.ReleaseAsync (cleanup.Token);
				await Record ("reservation-released", new { Owner = _owner, _peer.Host });
				}
			catch (Exception error)
				{
				try { await Record ("completion-failed", new { Type = error.GetType ().FullName, Owner = _owner, PhysicalRestorationConfirmed = physicalRestorationConfirmed }); } catch { }
				if (!preserveFailure) throw;
				}
			}
		public async ValueTask DisposeAsync ()
			{
			try { if (_client != null) await _client.DisposeAsync (); }
			finally { _lease?.Dispose (); _package?.Dispose (); }
			}
		}
	}