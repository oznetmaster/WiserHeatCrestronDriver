// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;

using NUnit.Framework;

using WiserHeatCrestronDriver.ConfigurationProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ConfigurationCompatibilityTests
	{
	private static DriverConfigurationSnapshot Snapshot () => new (123, "Test Wiser", ConfigurationCompatibility.Model, "1.3.006.0001", true, true, true,
		new Dictionary<string, string>
			{
			["_Host_"] = "hub.invalid",
			["HubSecret"] = "synthetic-test-secret",
			["TemperatureUnits"] = "Celsius",
			["BoostDelta"] = "2",
			["BoostDurationMinutes"] = "60",
			["EnableWholeHouseHotWater"] = "true",
			["AllowAwayMode"] = "false"
			}.Select (v => new DriverConfigurationItem (v.Key, v.Key, "String", true, false, false, true, JsonSerializer.SerializeToElement (v.Value))).ToArray ());
	[Test]
	public void ReorderedSettingsAndChangedVersion_KeepIdentity ()
		{
		var original = Snapshot ();
		Assert.That (ConfigurationCompatibility.Identity (original with
			{
			Version = "1.3.006.0002",
			Items = original.Items.Reverse ().ToArray ()
			}), Is.EqualTo (ConfigurationCompatibility.Identity (original)));
		}
	[TestCase ("HubSecret", "changed-synthetic-secret")]
	[TestCase ("_Host_", "other.invalid")]
	[TestCase ("BoostDelta", "3")]
	public void CurrentCredentialAndConnectionChanges_AreNotIgnored (string id, string value)
		{
		var original = Snapshot ();
		var changed = original with
			{
			Items = original.Items.Select (i => i.Id == id ? i with { CurrentValue = JsonSerializer.SerializeToElement (value) } : i).ToArray ()
			};
		Assert.That (ConfigurationCompatibility.Identity (changed), Is.Not.EqualTo (ConfigurationCompatibility.Identity (original)));
		}
	[TestCase ("Masked")]
	[TestCase ("Missing")]
	[TestCase ("Duplicate")]
	[TestCase ("Unknown")]
	[TestCase ("Unconfigured")]
	[TestCase ("Unavailable")]
	public void IncompleteOrAmbiguousConfiguration_IsRejected (string fault)
		{
		var snapshot = Snapshot ();
		snapshot = fault switch
			{
				"Masked" => snapshot with { Items = snapshot.Items.Select (i => i.Id == "HubSecret" ? i with { Masked = true, CurrentValue = null } : i).ToArray () },
				"Missing" => snapshot with { Items = snapshot.Items.Skip (1).ToArray () },
				"Duplicate" => snapshot with { Items = snapshot.Items.Append (snapshot.Items[0]).ToArray () },
				"Unknown" => snapshot with { Items = snapshot.Items.Select (i => i.Id == "HubSecret" ? i with { Id = "NewSecretFormat" } : i).ToArray () },
				"Unconfigured" => snapshot with { IsConfigured = false },
				_ => snapshot with { ItemsAvailable = false }
				};
		Assert.Throws<InvalidDataException> (() => ConfigurationCompatibility.Identity (snapshot));
		}
	[TestCase ("HubSecret", "********")]
	[TestCase ("TemperatureUnits", "Kelvin")]
	[TestCase ("BoostDelta", "NaN")]
	[TestCase ("BoostDurationMinutes", "-1")]
	[TestCase ("AllowAwayMode", "unknown")]
	public void UnsupportedCurrentValues_AreRejected (string id, string value)
		{
		var original = Snapshot ();
		var changed = original with
			{
			Items = original.Items.Select (i => i.Id == id ? i with { CurrentValue = JsonSerializer.SerializeToElement (value) } : i).ToArray ()
			};
		Assert.Throws<InvalidDataException> (() => ConfigurationCompatibility.Identity (changed));
		}
	[TestCase ("None")]
	[TestCase ("Hash")]
	[TestCase ("Schema")]
	[TestCase ("Version")]
	[TestCase ("Review")]
	[TestCase ("Family")]
	public void OnlyReviewedExactPackagePairWithIdenticalSchema_IsAccepted (string fault)
		{
		string directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "pair-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (directory);
		try
			{
			string Build (string file, string version, bool changedSchema, bool wrongFamily)
				{
				string path = Path.Combine (directory, file);
				using var zip = ZipFile.Open (path, ZipArchiveMode.Create);
				using (var assembly = zip.CreateEntry ("driver.dll").Open ())
					assembly.Write ([0x4d, 0x5a]);
				using var entry = zip.CreateEntry ("driver.dat").Open ();
				JsonSerializer.Serialize (entry, new
					{
					driverId = "8f153bae-6a59-44b5-90bc-c73f635a4d90",
					baseModel = wrongFamily ? "Other driver" : ConfigurationCompatibility.Model,
					manufacturer = "Test",
					driverVersion = version,
					configuration = new
						{
						items = Snapshot ().Items.Select (i => new { id = i.Id, valueType = changedSchema ? "Number" : "String" }).ToArray ()
						}
					});
				return path;
				}
			string old = Build ("old.pkg", "1.3.006.0001", false, false), next = Build ("new.pkg", fault == "Version" ? "1.3.006.0000" : "1.3.006.0002", fault == "Schema", fault == "Family");
			string Hash (string path) => Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (path)));
			var pair = new ReviewedPair (old, fault == "Hash" ? new string ('0', 64) : Hash (old), next, Hash (next), fault == "Review" ? "" : "Synthetic unchanged configuration-code review");
			if (fault == "None")
				Assert.That (ConfigurationCompatibility.ValidatePair (pair).Next.Version, Is.EqualTo ("1.3.006.0002"));
			else
				Assert.Throws<InvalidDataException> (() => ConfigurationCompatibility.ValidatePair (pair));
			}
		finally { Directory.Delete (directory, recursive: true); }
		}
	}