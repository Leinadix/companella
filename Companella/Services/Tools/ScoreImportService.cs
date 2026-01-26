using System.Security.Cryptography;
using System.Text;
using Companella.Extensions;
using Companella.Models.Session;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Platform;

namespace Companella.Services.Tools;

/// <summary>
/// Service for importing older scores from osu!'s scores.db as Companella sessions.
/// Only imports scores that have a corresponding .osr replay file.
/// </summary>
public class ScoreImportService
{
	private readonly OsuProcessDetector _processDetector;
	private readonly ReplayParserService _replayParser;
	private readonly SessionDatabaseService _sessionDatabase;
	private readonly MapsDatabaseService _mapsDatabase;

	// Mod flags
	private const int _modDT = 64;
	private const int _modNC = 512;
	private const int _modHT = 256;

	public ScoreImportService(
		OsuProcessDetector processDetector,
		ReplayParserService replayParser,
		SessionDatabaseService sessionDatabase,
		MapsDatabaseService mapsDatabase)
	{
		_processDetector = processDetector;
		_replayParser = replayParser;
		_sessionDatabase = sessionDatabase;
		_mapsDatabase = mapsDatabase;
	}

	/// <summary>
	/// Result of a score import operation.
	/// </summary>
	public class ImportResult
	{
		public bool Success { get; set; }
		public int TotalScoresRead { get; set; }
		public int ManiaScoresFound { get; set; }
		public int ScoresWithReplays { get; set; }
		public int ScoresWithBeatmaps { get; set; }
		public int SessionsCreated { get; set; }
		public int PlaysImported { get; set; }
		public string? Error { get; set; }
	}

	/// <summary>
	/// Progress callback for import operation.
	/// </summary>
	public class ImportProgress
	{
		public string Stage { get; set; } = string.Empty;
		public int Current { get; set; }
		public int Total { get; set; }
		public string CurrentItem { get; set; } = string.Empty;
	}

	/// <summary>
	/// Imports scores from scores.db as sessions.
	/// Only imports osu!mania scores that have corresponding replay files.
	/// </summary>
	/// <param name="progressCallback">Optional callback for progress updates.</param>
	/// <returns>Result of the import operation.</returns>
	public ImportResult ImportScoresAsSessions(Action<ImportProgress>? progressCallback = null)
	{
		var result = new ImportResult();

		try
		{
			var osuDir = _processDetector.GetOsuDirectory();
			if (string.IsNullOrEmpty(osuDir))
			{
				result.Error = "Could not find osu! directory";
				return result;
			}

			var scoresPath = Path.Combine(osuDir, "scores.db");
			if (!File.Exists(scoresPath))
			{
				result.Error = "scores.db not found";
				return result;
			}

			// Get replay folders
			var replayFolders = _replayParser.GetReplayFolders();
			if (replayFolders.Count == 0)
			{
				result.Error = "No replay folders found";
				return result;
			}

			// Step 1: Read all scores
			progressCallback?.Invoke(new ImportProgress
			{
				Stage = "Reading scores.db...",
				Current = 0,
				Total = 0
			});

			var scores = ReadScoresDb(scoresPath);
			result.TotalScoresRead = scores.Count;
			Logger.Info($"[ScoreImport] Read {scores.Count} total scores from scores.db");

			// Step 2: Filter to mania mode (mode == 3)
			var maniaScores = scores.Where(s => s.Mode == 3).ToList();
			result.ManiaScoresFound = maniaScores.Count;
			Logger.Info($"[ScoreImport] Found {maniaScores.Count} osu!mania scores");

			if (maniaScores.Count == 0)
			{
				result.Success = true;
				return result;
			}

			// Step 3: Filter to scores with replay files
			progressCallback?.Invoke(new ImportProgress
			{
				Stage = "Checking for replay files...",
				Current = 0,
				Total = maniaScores.Count
			});

			var scoresWithReplays = new List<ImportScore>();
			for (var i = 0; i < maniaScores.Count; i++)
			{
				var score = maniaScores[i];

				// Log debug info for the first score to help diagnose filename format issues
				if (i == 0) LogReplayDebugInfo(score.BeatmapHash, score.Timestamp, replayFolders);

				var replayPath = FindReplayFile(score.BeatmapHash, score.Timestamp, replayFolders);

				if (!string.IsNullOrEmpty(replayPath))
					scoresWithReplays.Add(new ImportScore
					{
						Score = score,
						ReplayPath = replayPath
					});

				if (i % 100 == 0)
					progressCallback?.Invoke(new ImportProgress
					{
						Stage = "Checking for replay files...",
						Current = i,
						Total = maniaScores.Count,
						CurrentItem = $"{scoresWithReplays.Count} found"
					});
			}

			result.ScoresWithReplays = scoresWithReplays.Count;
			Logger.Info($"[ScoreImport] Found {scoresWithReplays.Count} scores with replay files");

			if (scoresWithReplays.Count == 0)
			{
				result.Success = true;
				return result;
			}

			// Step 4: Find beatmap paths for each score using the maps.db index (fast!)
			progressCallback?.Invoke(new ImportProgress
			{
				Stage = "Loading beatmap index from maps.db...",
				Current = 0,
				Total = scoresWithReplays.Count
			});

			// Load beatmap hash -> path mapping from maps.db (much faster than scanning files)
			var beatmapHashIndex = _mapsDatabase.GetBeatmapPathsByHash();
			Logger.Info($"[ScoreImport] Loaded {beatmapHashIndex.Count} beatmaps from maps.db index");

			var foundCount = 0;
			for (var i = 0; i < scoresWithReplays.Count; i++)
			{
				var importScore = scoresWithReplays[i];
				var hash = importScore.Score.BeatmapHash.ToLowerInvariant();

				if (beatmapHashIndex.TryGetValue(hash, out var beatmapPath))
				{
					importScore.BeatmapPath = beatmapPath;
					foundCount++;
				}

				if (i % 100 == 0)
					progressCallback?.Invoke(new ImportProgress
					{
						Stage = "Matching beatmaps...",
						Current = i,
						Total = scoresWithReplays.Count,
						CurrentItem = $"{foundCount} matched"
					});
			}

			// Filter to scores with valid beatmap paths
			var validScores = scoresWithReplays.Where(s => !string.IsNullOrEmpty(s.BeatmapPath)).ToList();
			result.ScoresWithBeatmaps = validScores.Count;
			Logger.Info($"[ScoreImport] Found {validScores.Count} scores with valid beatmap paths");

			if (validScores.Count == 0)
			{
				result.Success = true;
				return result;
			}

			// Step 5: Calculate accuracy for each score
			foreach (var importScore in validScores)
			{
				importScore.Accuracy = CalculateManiaAccuracy(importScore.Score);
				importScore.Rate = GetRateFromMods(importScore.Score.Mods);
			}

			// Step 6: Group by calendar day
			// scores.db stores timestamps as .NET DateTime.Ticks (not FILETIME)
			var scoresByDay = validScores
				.GroupBy(s => new DateTime(s.Score.Timestamp, DateTimeKind.Utc).ToLocalTime().Date)
				.OrderBy(g => g.Key)
				.ToList();

			Logger.Info($"[ScoreImport] Grouped into {scoresByDay.Count} days");

			// Step 7: Create sessions for each day
			var totalPlays = 0;
			var sessionsCreated = 0;

			for (var dayIndex = 0; dayIndex < scoresByDay.Count; dayIndex++)
			{
				var dayGroup = scoresByDay[dayIndex];
				var dayScores = dayGroup.OrderBy(s => s.Score.Timestamp).ToList();

				progressCallback?.Invoke(new ImportProgress
				{
					Stage = $"Creating sessions ({dayIndex + 1}/{scoresByDay.Count})...",
					Current = dayIndex,
					Total = scoresByDay.Count,
					CurrentItem = $"{dayGroup.Key:yyyy-MM-dd}: {dayScores.Count} plays"
				});

				// Calculate MSD for each play
				var plays = new List<SessionPlayResult>();
				var startTime = new DateTime(dayScores.First().Score.Timestamp, DateTimeKind.Utc);

				for (var i = 0; i < dayScores.Count; i++)
				{
					var importScore = dayScores[i];
					var playTime = new DateTime(importScore.Score.Timestamp, DateTimeKind.Utc);
					var sessionTime = playTime - startTime;

					// Calculate MSD
					float highestMsd = 0;
					var dominantSkillset = "unknown";

					try
					{
						if (ToolPaths.MsdCalculatorExists && !string.IsNullOrEmpty(importScore.BeatmapPath))
						{
							var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);
							var msdResult =
								analyzer.AnalyzeSingleRate(importScore.BeatmapPath, importScore.Rate, 30000);

							if (msdResult?.Scores != null)
							{
								var dominant = msdResult.Scores.GetDominantSkillset();
								highestMsd = dominant.Value;
								dominantSkillset = dominant.Name;
							}
						}
					}
					catch (Exception ex)
					{
						Logger.Info($"[ScoreImport] MSD calculation failed: {ex.Message}");
					}

					// Calculate beatmap hash
					var beatmapHash = "";
					try
					{
						if (File.Exists(importScore.BeatmapPath))
						{
							beatmapHash = File.OpenRead(importScore.BeatmapPath).Md5();
						}
					}
					catch (Exception ex)
					{
						Logger.Info($"[ScoreImport] Hash calculation failed: {ex.Message}");
					}

					plays.Add(new SessionPlayResult(
						importScore.BeatmapPath!,
						beatmapHash,
						importScore.Accuracy,
						0, // Misses - not available during import
						0, // PauseCount - not available during import
						PlayStatus.Completed, // Status - assume completed for imports
						sessionTime,
						playTime,
						highestMsd,
						dominantSkillset
					));

					// Update progress within day
					if (i % 10 == 0)
						progressCallback?.Invoke(new ImportProgress
						{
							Stage = $"Creating sessions ({dayIndex + 1}/{scoresByDay.Count})...",
							Current = dayIndex,
							Total = scoresByDay.Count,
							CurrentItem = $"{dayGroup.Key:yyyy-MM-dd}: {i + 1}/{dayScores.Count} plays (MSD)"
						});
				}

				// Create session
				if (plays.Count > 0)
				{
					var endTime = new DateTime(dayScores.Last().Score.Timestamp, DateTimeKind.Utc);
					var sessionId = _sessionDatabase.SaveSession(startTime, endTime, plays);

					if (sessionId > 0)
					{
						sessionsCreated++;
						totalPlays += plays.Count;
						Logger.Info(
							$"[ScoreImport] Created session {sessionId} for {dayGroup.Key:yyyy-MM-dd} with {plays.Count} plays");
					}
				}
			}

			result.SessionsCreated = sessionsCreated;
			result.PlaysImported = totalPlays;
			result.Success = true;

			Logger.Info($"[ScoreImport] Import complete: {sessionsCreated} sessions, {totalPlays} plays");
		}
		catch (Exception ex)
		{
			result.Error = ex.Message;
			Logger.Info($"[ScoreImport] Error: {ex.Message}");
		}

		return result;
	}

	// Difference between .NET DateTime.Ticks (since year 0001) and Windows FILETIME (since year 1601)
	private const long _ticksToFileTimeOffset = 504911232000000000L;

	/// <summary>
	/// Finds a replay file by beatmap hash and timestamp.
	/// Tries both .NET Ticks format and Windows FILETIME format.
	/// </summary>
	private static string? FindReplayFile(string beatmapHash, long timestamp, List<string> replayFolders)
	{
		// scores.db may store timestamps as .NET DateTime.Ticks, but replay filenames use Windows FILETIME
		// Try both formats
		var timestampsToTry = new List<long> { timestamp };

		// If timestamp looks like .NET Ticks (> 500 trillion), convert to FILETIME
		if (timestamp > _ticksToFileTimeOffset) timestampsToTry.Add(timestamp - _ticksToFileTimeOffset);

		var hashLower = beatmapHash.ToLowerInvariant();

		foreach (var ts in timestampsToTry)
			foreach (var folder in replayFolders)
			{
				// Try original case
				var path = Path.Combine(folder, $"{beatmapHash}-{ts}.osr");
				if (File.Exists(path))
					return path;

				// Try lowercase hash
				var pathLower = Path.Combine(folder, $"{hashLower}-{ts}.osr");
				if (File.Exists(pathLower))
					return pathLower;
			}

		return null;
	}

	private bool _hasLoggedSampleFiles;

	/// <summary>
	/// Logs sample replay files and what we're looking for (debug).
	/// </summary>
	private void LogReplayDebugInfo(string beatmapHash, long timestamp, List<string> replayFolders)
	{
		if (_hasLoggedSampleFiles)
			return;
		_hasLoggedSampleFiles = true;

		var convertedTimestamp = timestamp > _ticksToFileTimeOffset ? timestamp - _ticksToFileTimeOffset : timestamp;

		Logger.Info($"[ScoreImport] DEBUG: Beatmap hash: {beatmapHash}");
		Logger.Info($"[ScoreImport] DEBUG: Raw timestamp: {timestamp}");
		Logger.Info($"[ScoreImport] DEBUG: Converted timestamp (FILETIME): {convertedTimestamp}");
		Logger.Info(
			$"[ScoreImport] DEBUG: Looking for: {beatmapHash}-{timestamp}.osr OR {beatmapHash}-{convertedTimestamp}.osr");

		foreach (var folder in replayFolders)
			try
			{
				var files = Directory.GetFiles(folder, "*.osr").Take(5).ToList();
				Logger.Info($"[ScoreImport] DEBUG: Sample files in {folder}:");
				foreach (var file in files) Logger.Info($"[ScoreImport] DEBUG:   {Path.GetFileName(file)}");
			}
			catch (Exception ex)
			{
				Logger.Info($"[ScoreImport] DEBUG: Error listing {folder}: {ex.Message}");
			}
	}

	/// <summary>
	/// Calculates osu!mania v1 accuracy from hit counts.
	/// </summary>
	private static double CalculateManiaAccuracy(ImportScoreData score)
	{
		// In mania: CountGeki = MAX/300g, Count300 = 300, CountKatu = 200
		// Mania v1: MAX and 300 are both worth 300
		var weightedScore = (score.CountGeki + score.Count300) * 300.0
							+ score.CountKatu * 200.0
							+ score.Count100 * 100.0
							+ score.Count50 * 50.0;

		var totalHits = score.CountGeki + score.Count300 + score.CountKatu
						+ score.Count100 + score.Count50 + score.CountMiss;

		if (totalHits == 0)
			return 0;

		return weightedScore / (totalHits * 300.0) * 100.0;
	}

	/// <summary>
	/// Gets the rate multiplier from mods.
	/// </summary>
	private static float GetRateFromMods(int mods)
	{
		if ((mods & _modNC) != 0 || (mods & _modDT) != 0)
			return 1.5f;

		if ((mods & _modHT) != 0)
			return 0.75f;

		return 1.0f;
	}

	/// <summary>
	/// Result of a replay lookup operation.
	/// </summary>
	public class ReplayLookupResult
	{
		public string ReplayHash { get; set; } = string.Empty;
		public string? ReplayPath { get; set; }
		public long Timestamp { get; set; }
	}

	/// <summary>
	/// Finds replay info from scores.db by matching beatmap hash and accuracy.
	/// Returns all matching scores with their replay hashes.
	/// </summary>
	/// <param name="beatmapHash">The beatmap MD5 hash to look for.</param>
	/// <param name="accuracy">The accuracy to match (with some tolerance).</param>
	/// <returns>List of matching replay info, ordered by timestamp descending.</returns>
	public List<ReplayLookupResult> FindReplayInScoresDb(string beatmapHash, double accuracy)
	{
		var results = new List<ReplayLookupResult>();

		try
		{
			var osuDir = _processDetector.GetOsuDirectory();
			if (string.IsNullOrEmpty(osuDir))
				return results;

			var scoresPath = Path.Combine(osuDir, "scores.db");
			if (!File.Exists(scoresPath))
				return results;

			var replayFolders = _replayParser.GetReplayFolders();
			var scores = ReadScoresDb(scoresPath);

			// Find scores matching the beatmap hash
			var hashLower = beatmapHash.ToLowerInvariant();
			var matchingScores = scores
				.Where(s => s.BeatmapHash.Equals(hashLower, StringComparison.OrdinalIgnoreCase)
							&& !string.IsNullOrEmpty(s.ReplayHash))
				.ToList();

			Logger.Info(
				$"[ScoreImport] Found {matchingScores.Count} scores for beatmap hash {hashLower}, looking for accuracy {accuracy:F4}%");

			foreach (var score in matchingScores)
			{
				// Calculate accuracy for this score
				var scoreAccuracy = CalculateManiaAccuracy(score);

				// Match if accuracy is within 0.5% (account for rounding differences)
				var diff = Math.Abs(scoreAccuracy - accuracy);
				if (diff < 0.5)
				{
					Logger.Info($"[ScoreImport] Matched! Score accuracy: {scoreAccuracy:F4}%, diff: {diff:F4}%");
					var result = new ReplayLookupResult
					{
						ReplayHash = score.ReplayHash,
						Timestamp = score.Timestamp
					};

					// Try to find the actual replay file
					result.ReplayPath = FindReplayFile(score.BeatmapHash, score.Timestamp, replayFolders);

					results.Add(result);
				}
			}

			// Order by timestamp descending (most recent first)
			results = results.OrderByDescending(r => r.Timestamp).ToList();
		}
		catch (Exception ex)
		{
			Logger.Info($"[ScoreImport] Error finding replay in scores.db: {ex.Message}");
		}

		return results;
	}

	/// <summary>
	/// Finds all replay info from scores.db for plays without replays.
	/// Returns a dictionary mapping (beatmapHash, accuracy) to replay info.
	/// </summary>
	public Dictionary<string, List<ReplayLookupResult>> GetAllReplayInfo()
	{
		var results = new Dictionary<string, List<ReplayLookupResult>>(StringComparer.OrdinalIgnoreCase);

		try
		{
			var osuDir = _processDetector.GetOsuDirectory();
			if (string.IsNullOrEmpty(osuDir))
				return results;

			var scoresPath = Path.Combine(osuDir, "scores.db");
			if (!File.Exists(scoresPath))
				return results;

			var replayFolders = _replayParser.GetReplayFolders();
			var scores = ReadScoresDb(scoresPath);

			// Group by beatmap hash
			foreach (var score in scores.Where(s => !string.IsNullOrEmpty(s.ReplayHash)))
			{
				var hashLower = score.BeatmapHash.ToLowerInvariant();

				if (!results.ContainsKey(hashLower))
					results[hashLower] = new List<ReplayLookupResult>();

				var accuracy = CalculateManiaAccuracy(score);
				var replayPath = FindReplayFile(score.BeatmapHash, score.Timestamp, replayFolders);

				results[hashLower].Add(new ReplayLookupResult
				{
					ReplayHash = score.ReplayHash,
					ReplayPath = replayPath,
					Timestamp = score.Timestamp
				});
			}

			Logger.Info($"[ScoreImport] Loaded {results.Count} beatmaps with replay info from scores.db");
		}
		catch (Exception ex)
		{
			Logger.Info($"[ScoreImport] Error reading scores.db: {ex.Message}");
		}

		return results;
	}

	/// <summary>
	/// Reads all scores from scores.db (read-only, no backup).
	/// </summary>
	private static List<ImportScoreData> ReadScoresDb(string scoresPath)
	{
		var scores = new List<ImportScoreData>();

		using var stream = File.OpenRead(scoresPath);
		using var reader = new BinaryReader(stream, Encoding.UTF8);

		// Version (Int32)
		reader.ReadInt32();

		// Number of beatmaps (Int32)
		var beatmapCount = reader.ReadInt32();

		for (var i = 0; i < beatmapCount; i++)
		{
			// Beatmap MD5 hash (String)
			var beatmapHash = ReadOsuString(reader);

			// Number of scores for this beatmap (Int32)
			var scoreCount = reader.ReadInt32();

			for (var j = 0; j < scoreCount; j++)
			{
				var score = new ImportScoreData { BeatmapHash = beatmapHash };

				// Mode (Byte)
				score.Mode = reader.ReadByte();

				// Score version (Int32)
				reader.ReadInt32();

				// Beatmap MD5 again (String)
				score.BeatmapHash = ReadOsuString(reader);

				// Player name (String)
				score.PlayerName = ReadOsuString(reader);

				// Replay MD5 (String)
				score.ReplayHash = ReadOsuString(reader);

				// Hit counts
				score.Count300 = reader.ReadInt16();
				score.Count100 = reader.ReadInt16();
				score.Count50 = reader.ReadInt16();
				score.CountGeki = reader.ReadInt16();
				score.CountKatu = reader.ReadInt16();
				score.CountMiss = reader.ReadInt16();

				// Score
				reader.ReadInt32();

				// Max combo
				reader.ReadInt16();

				// Perfect
				reader.ReadBoolean();

				// Mods
				score.Mods = reader.ReadInt32();

				// HP graph (String)
				ReadOsuString(reader);

				// Timestamp (Int64 - Windows ticks)
				score.Timestamp = reader.ReadInt64();

				// Replay data length (Int32)
				reader.ReadInt32();

				// Online score ID (Int64)
				reader.ReadInt64();

				// Additional info for target practice
				if ((score.Mods & (1 << 23)) != 0) reader.ReadDouble();

				scores.Add(score);
			}
		}

		return scores;
	}

	/// <summary>
	/// Reads an osu! string from a binary reader.
	/// </summary>
	private static string ReadOsuString(BinaryReader reader)
	{
		var flag = reader.ReadByte();
		if (flag == 0x00)
			return string.Empty;

		if (flag != 0x0b)
			throw new InvalidDataException($"Invalid string flag: {flag}");

		var length = ReadULEB128(reader);
		var bytes = reader.ReadBytes((int)length);
		return Encoding.UTF8.GetString(bytes);
	}

	/// <summary>
	/// Reads an unsigned LEB128 encoded integer.
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
	/// Internal class for score data during import.
	/// </summary>
	private class ImportScoreData
	{
		public string BeatmapHash { get; set; } = string.Empty;
		public byte Mode { get; set; }
		public string PlayerName { get; set; } = string.Empty;
		public string ReplayHash { get; set; } = string.Empty;
		public short Count300 { get; set; }
		public short Count100 { get; set; }
		public short Count50 { get; set; }
		public short CountGeki { get; set; }
		public short CountKatu { get; set; }
		public short CountMiss { get; set; }
		public int Mods { get; set; }
		public long Timestamp { get; set; }
	}

	/// <summary>
	/// Internal class for tracking import progress per score.
	/// </summary>
	private class ImportScore
	{
		public ImportScoreData Score { get; set; } = null!;
		public string ReplayPath { get; set; } = string.Empty;
		public string? BeatmapPath { get; set; }
		public double Accuracy { get; set; }
		public float Rate { get; set; } = 1.0f;
	}
}

/// <summary>
/// Extension method for DateTime UTC conversion.
/// </summary>
internal static class DateTimeExtensions
{
	public static DateTime ToUtc(this DateTime dt)
	{
		if (dt.Kind == DateTimeKind.Utc)
			return dt;
		if (dt.Kind == DateTimeKind.Local)
			return dt.ToUniversalTime();
		return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
	}
}
