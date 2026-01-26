using System.Security.Cryptography;
using System.Text;
using Companella.Extensions;
using Companella.Services.Common;
using Companella.Services.Platform;

namespace Companella.Services.Beatmap;

/// <summary>
/// Service for managing osu! collections (collection.db).
/// Can create/update a "Companella!" collection with recommended maps.
/// </summary>
public class OsuCollectionService
{
	private const string _collectionName = "Companella!";

	private readonly OsuProcessDetector _processDetector;

	public OsuCollectionService(OsuProcessDetector processDetector)
	{
		_processDetector = processDetector;
	}

	/// <summary>
	/// Updates the Companella! collection with the specified beatmap paths.
	/// </summary>
	/// <param name="beatmapPaths">List of .osu file paths to add to the collection</param>
	/// <returns>True if successful, false otherwise</returns>
	public bool UpdateCollection(IEnumerable<string> beatmapPaths)
	{
		var osuDir = _processDetector.GetOsuDirectory();
		if (string.IsNullOrEmpty(osuDir))
		{
			Logger.Info("[Collection] Could not find osu! directory");
			return false;
		}

		var collectionPath = Path.Combine(osuDir, "collection.db");

		try
		{
			// Calculate MD5 hashes for all beatmaps
			var beatmapHashes = new List<string>();
			foreach (var path in beatmapPaths)
				if (File.Exists(path))
				{
					var hash = CalculateBeatmapHash(path);
					if (!string.IsNullOrEmpty(hash)) beatmapHashes.Add(hash);
				}

			if (beatmapHashes.Count == 0)
			{
				Logger.Info("[Collection] No valid beatmaps to add");
				return false;
			}

			// Read existing collections
			var collections = ReadCollections(collectionPath);

			// Find or create Companella! collection
			var companellaCollection = collections.FirstOrDefault(c => c.Name == _collectionName);
			if (companellaCollection == null)
			{
				companellaCollection = new OsuCollectionWrapper { Name = _collectionName };
				collections.Add(companellaCollection);
			}

			// Update the collection with new hashes (replace existing)
			companellaCollection.BeatmapHashes = beatmapHashes;

			// Write collections back
			WriteCollections(collectionPath, collections);

			Logger.Info($"[Collection] Updated '{_collectionName}' with {beatmapHashes.Count} maps");

			return true;
		}
		catch (Exception ex)
		{
			Logger.Info($"[Collection] Error updating collection: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Calculates the MD5 hash of a beatmap file (used by osu! for identification).
	/// </summary>
	private static string CalculateBeatmapHash(string beatmapPath)
	{
		try
		{
			var hashString = File.OpenRead(beatmapPath).Md5();
			Logger.Info($"[Collection] Hashed {Path.GetFileName(beatmapPath)}: {hashString}");
			return hashString;
		}
		catch (Exception ex)
		{
			Logger.Info($"[Collection] Error hashing {Path.GetFileName(beatmapPath)}: {ex.Message}");
			return string.Empty;
		}
	}

	/// <summary>
	/// Reads all collections from collection.db.
	/// </summary>
	private static List<OsuCollectionWrapper> ReadCollections(string collectionPath)
	{
		var collections = new List<OsuCollectionWrapper>();

		if (!File.Exists(collectionPath))
		{
			Logger.Info("[Collection] collection.db not found, will create new");
			return collections;
		}

		try
		{
			using var stream = File.OpenRead(collectionPath);
			using var reader = new BinaryReader(stream, Encoding.UTF8);

			// Version (Int32)
			var version = reader.ReadInt32();

			// Number of collections (Int32)
			var collectionCount = reader.ReadInt32();

			for (var i = 0; i < collectionCount; i++)
			{
				var collection = new OsuCollectionWrapper();

				// Collection name (ULEB128 string)
				collection.Name = ReadOsuString(reader);

				// Number of beatmaps (Int32)
				var beatmapCount = reader.ReadInt32();

				for (var j = 0; j < beatmapCount; j++)
				{
					// Beatmap MD5 hash (ULEB128 string)
					var hash = ReadOsuString(reader);
					if (!string.IsNullOrEmpty(hash)) collection.BeatmapHashes.Add(hash);
				}

				collections.Add(collection);
			}

			Logger.Info($"[Collection] Read {collections.Count} collections from collection.db");
		}
		catch (Exception ex)
		{
			Logger.Info($"[Collection] Error reading collection.db: {ex.Message}");
		}

		return collections;
	}

	/// <summary>
	/// Writes all collections to collection.db.
	/// </summary>
	private static void WriteCollections(string collectionPath, List<OsuCollectionWrapper> collections)
	{
		// Create backup first
		if (File.Exists(collectionPath))
		{
			var backupPath = collectionPath + ".bak";
			File.Copy(collectionPath, backupPath, true);
		}

		using var stream = File.Create(collectionPath);
		using var writer = new BinaryWriter(stream, Encoding.UTF8);

		// Version (Int32) - use version 20140609 (standard osu! version)
		writer.Write(20140609);

		// Number of collections (Int32)
		writer.Write(collections.Count);

		foreach (var collection in collections)
		{
			// Collection name (ULEB128 string)
			WriteOsuString(writer, collection.Name);

			// Number of beatmaps (Int32)
			writer.Write(collection.BeatmapHashes.Count);

			foreach (var hash in collection.BeatmapHashes)
				// Beatmap MD5 hash (ULEB128 string)
				WriteOsuString(writer, hash);
		}

		Logger.Info($"[Collection] Wrote {collections.Count} collections to collection.db");
	}

	/// <summary>
	/// Reads an osu! string (0x00 for null, or 0x0b + ULEB128 length + UTF8 bytes).
	/// </summary>
	private static string ReadOsuString(BinaryReader reader)
	{
		var flag = reader.ReadByte();
		if (flag == 0x00) return string.Empty;

		if (flag != 0x0b) throw new InvalidDataException($"Invalid string flag: {flag}");

		var length = ReadULEB128(reader);
		var bytes = reader.ReadBytes((int)length);
		return Encoding.UTF8.GetString(bytes);
	}

	/// <summary>
	/// Writes an osu! string (0x0b + ULEB128 length + UTF8 bytes).
	/// </summary>
	private static void WriteOsuString(BinaryWriter writer, string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			writer.Write((byte)0x00);
			return;
		}

		writer.Write((byte)0x0b);
		var bytes = Encoding.UTF8.GetBytes(value);
		WriteULEB128(writer, (uint)bytes.Length);
		writer.Write(bytes);
	}

	/// <summary>
	/// Reads a ULEB128 encoded integer.
	/// </summary>
	private static uint ReadULEB128(BinaryReader reader)
	{
		uint result = 0;
		var shift = 0;

		while (true)
		{
			var b = reader.ReadByte();
			result |= (uint)(b & 0x7F) << shift;

			if ((b & 0x80) == 0)
				break;

			shift += 7;
		}

		return result;
	}

	/// <summary>
	/// Writes a ULEB128 encoded integer.
	/// </summary>
	private static void WriteULEB128(BinaryWriter writer, uint value)
	{
		do
		{
			var b = (byte)(value & 0x7F);
			value >>= 7;

			if (value != 0)
				b |= 0x80;

			writer.Write(b);
		} while (value != 0);
	}

	/// <summary>
	/// Restarts osu! as fast as possible by killing the process and relaunching it.
	/// </summary>
	/// <param name="arguments">Optional command line arguments to pass to osu! on startup.</param>
	public void RestartOsu(string? arguments = null)
	{
		try
		{
			var osuDir = _processDetector.GetOsuDirectory();
			if (string.IsNullOrEmpty(osuDir))
			{
				Logger.Info("[Collection] Could not find osu! directory");
				return;
			}

			var osuExePath = Path.Combine(osuDir, "osu!.exe");
			if (!File.Exists(osuExePath))
			{
				Logger.Info("[Collection] osu!.exe not found");
				return;
			}

			// Find and kill the osu! process
			var osuProcesses = System.Diagnostics.Process.GetProcessesByName("osu!");
			if (osuProcesses.Length == 0) osuProcesses = System.Diagnostics.Process.GetProcessesByName("osu");

			foreach (var proc in osuProcesses)
				try
				{
					Logger.Info($"[Collection] Killing osu! process (PID: {proc.Id})");
					proc.Kill();
					proc.WaitForExit(3000); // Wait up to 3 seconds for process to exit
				}
				catch (Exception ex)
				{
					Logger.Info($"[Collection] Error killing process: {ex.Message}");
				}
				finally
				{
					proc.Dispose();
				}

			// Immediately restart osu!
			var hasArgs = !string.IsNullOrWhiteSpace(arguments);
			Logger.Info($"[Collection] Restarting osu!{(hasArgs ? $" with args: {arguments}" : "")}...");

			var startInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = osuExePath,
				WorkingDirectory = osuDir,
				UseShellExecute = true
			};

			if (hasArgs) startInfo.Arguments = arguments;

			System.Diagnostics.Process.Start(startInfo);

			Logger.Info("[Collection] osu! restart initiated");
		}
		catch (Exception ex)
		{
			Logger.Info($"[Collection] Error restarting osu!: {ex.Message}");
		}
	}

	/// <summary>
	/// Creates a session collection with a timestamped name.
	/// </summary>
	/// <param name="beatmapPaths">List of .osu file paths to add to the collection.</param>
	/// <param name="timestamp">Optional timestamp for the collection name. Defaults to now.</param>
	/// <returns>The collection name if successful, null otherwise.</returns>
	public string? CreateSessionCollection(IEnumerable<string> beatmapPaths, DateTime? timestamp = null)
	{
		var osuDir = _processDetector.GetOsuDirectory();
		if (string.IsNullOrEmpty(osuDir))
		{
			Logger.Info("[Collection] Could not find osu! directory");
			return null;
		}

		var collectionPath = Path.Combine(osuDir, "collection.db");
		var date = timestamp ?? DateTime.Now;
		var sessionCollectionName = $"Companella!session#{date:yyyyMMdd-HHmm}";

		try
		{
			// Calculate MD5 hashes for all beatmaps
			var beatmapHashes = new List<string>();
			foreach (var path in beatmapPaths)
				if (File.Exists(path))
				{
					var hash = CalculateBeatmapHash(path);
					if (!string.IsNullOrEmpty(hash)) beatmapHashes.Add(hash);
				}

			if (beatmapHashes.Count == 0)
			{
				Logger.Info("[Collection] No valid beatmaps to add to session collection");
				return null;
			}

			// Read existing collections
			var collections = ReadCollections(collectionPath);

			// Create the new session collection
			var sessionCollection = new OsuCollectionWrapper
			{
				Name = sessionCollectionName,
				BeatmapHashes = beatmapHashes
			};
			collections.Add(sessionCollection);

			// Write collections back
			WriteCollections(collectionPath, collections);

			Logger.Info(
				$"[Collection] Created session collection '{sessionCollectionName}' with {beatmapHashes.Count} maps");

			return sessionCollectionName;
		}
		catch (Exception ex)
		{
			Logger.Info($"[Collection] Error creating session collection: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Clears all maps from the Companella! collection.
	/// </summary>
	public bool ClearCollection()
	{
		var osuDir = _processDetector.GetOsuDirectory();
		if (string.IsNullOrEmpty(osuDir)) return false;

		var collectionPath = Path.Combine(osuDir, "collection.db");

		try
		{
			var collections = ReadCollections(collectionPath);
			var companellaCollection = collections.FirstOrDefault(c => c.Name == _collectionName);

			if (companellaCollection != null)
			{
				companellaCollection.BeatmapHashes.Clear();
				WriteCollections(collectionPath, collections);
				Logger.Info("[Collection] Cleared Companella! collection");
			}

			return true;
		}
		catch (Exception ex)
		{
			Logger.Info($"[Collection] Error clearing collection: {ex.Message}");
			return false;
		}
	}
}

/// <summary>
/// Represents an osu! collection.
/// </summary>
public class OsuCollectionWrapper
{
	public string Name { get; set; } = string.Empty;
	public List<string> BeatmapHashes { get; set; } = new();
}
