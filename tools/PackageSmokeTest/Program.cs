// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.IO.Compression;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;

// Exercise the actual merged package's entry point, including its embedded
// definition lookup. Compiling driver sources into a test assembly is insufficient.
if (args.Length != 1)
	throw new ArgumentException ("Provide the Wiser .pkg file to validate.");

string package = Path.GetFullPath (args[0]);
string scratch = Path.Combine (Path.GetTempPath (), "wiser-package-check-" + Guid.NewGuid ().ToString ("N"));
Directory.CreateDirectory (scratch);
try
	{
	using (var archive = ZipFile.OpenRead (package))
		{
		var metadata = archive.Entries.Single (entry => entry.FullName.EndsWith (".dat", StringComparison.OrdinalIgnoreCase));
		using var document = JsonDocument.Parse (metadata.Open ());
		var root = document.RootElement;
		if (root.GetProperty ("driverId").GetString () != "8f153bae-6a59-44b5-90bc-c73f635a4d90")
			throw new InvalidDataException ("This smoke test is for the Wiser driver.");
		string assemblyName = root.GetProperty ("assemblyFileName").GetString () ?? throw new InvalidDataException ();
		if (assemblyName != Path.GetFileName (assemblyName) || assemblyName.Contains ('\\'))
			throw new InvalidDataException ("Expected a root assembly filename.");
		ZipFile.ExtractToDirectory (package, scratch);
		using var assemblyBytes = File.OpenRead (Path.Combine (scratch, assemblyName));
		var assembly = AssemblyLoadContext.Default.LoadFromStream (assemblyBytes);
		assemblyBytes.Dispose ();
		var entryType = assembly.GetType (root.GetProperty ("className").GetString ()!, throwOnError: true)!;
		var entry = (DriverAssemblyEntryPoint)Activator.CreateInstance (entryType)!;
		using var logger = new DriverLogger ("packaged-entrypoint-test");
		using var controller = entry.CreateDriverControllerInstance (new DriverControllerCreationArgs (
			"packaged-entrypoint-test", scratch, logger.AppLogger, null));
		var state = controller.GetState (DriverController.RootControllerId);
		if (state.PropertyValues["onlineIndicator:isOnline"].GetValue<bool> () ||
			state.PropertyValues["readyIndicator:isReady"].GetValue<bool> ())
			throw new InvalidDataException ("An unconfigured driver must remain offline and not ready.");
		Console.WriteLine (JsonSerializer.Serialize (new
			{
			PackageSha256 = Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (package))),
			Version = root.GetProperty ("driverVersion").GetString (),
			Assembly = assemblyName,
			EntryPointCreated = true,
			UnconfiguredStateVerified = true
			}));
		}
	}
catch (Exception error)
	{
	Console.Error.WriteLine (error);
	Environment.ExitCode = 1;
	}
finally
	{
	// The loaded assembly may remain locked until this short-lived process exits.
	// Retain diagnostic scratch rather than let cleanup replace the test result.
	try { Directory.Delete (scratch, recursive: true); }
	catch (IOException) { Console.Error.WriteLine ("Package check scratch retained: " + scratch); }
	catch (UnauthorizedAccessException) { Console.Error.WriteLine ("Package check scratch retained: " + scratch); }
	}