using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Companella.Services.Common;

namespace Companella.Services.Tools;

/// <summary>
/// ffmpeg helpers to probe duration and reverse audio for the Reverse beatmap mod.
/// </summary>
public static class AudioReverseUtilities
{
	/// <summary>
	/// Returns duration in milliseconds, or 0 if the file cannot be read.
	/// </summary>
	public static double GetAudioDurationMs(string audioPath, string ffmpegPath = "ffmpeg")
	{
		if (!File.Exists(audioPath))
			return 0;

		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = ffmpegPath,
				Arguments = $"-i \"{audioPath}\" -f null -",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			using var process = new Process { StartInfo = startInfo };
			var errorBuilder = new StringBuilder();

			process.ErrorDataReceived += (_, e) =>
			{
				if (e.Data != null)
					errorBuilder.AppendLine(e.Data);
			};

			process.Start();
			process.BeginErrorReadLine();

			if (!process.WaitForExit(15000))
			{
				try
				{
					process.Kill();
				}
				catch
				{
					// ignored
				}

				return 0;
			}

			var output = errorBuilder.ToString();
			var match = Regex.Match(output, @"Duration:\s*(\d+):(\d+):(\d+)\.(\d+)");
			if (!match.Success)
				return 0;

			var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
			var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
			var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
			var frac = match.Groups[4].Value;
			var fracPadded = frac.PadRight(2, '0')[..2];
			var centiseconds = int.Parse(fracPadded, CultureInfo.InvariantCulture);

			return (hours * 3600 + minutes * 60 + seconds) * 1000 + centiseconds * 10;
		}
		catch (Exception ex)
		{
			Logger.Info($"[AudioReverse] Duration probe failed: {ex.Message}");
			return 0;
		}
	}

	/// <summary>
	/// Reverses audio with ffmpeg <c>areverse</c>. Output extension selects encoder.
	/// </summary>
	public static async Task CreateReversedAudioFileAsync(string inputPath, string outputPath, string ffmpegPath = "ffmpeg",
		Action<string>? progressCallback = null)
	{
		if (!File.Exists(inputPath))
			throw new FileNotFoundException($"Audio file not found: {inputPath}");

		var arguments = $"-y -i \"{inputPath}\" -af areverse -vn \"{outputPath}\"";
		Logger.Info($"[AudioReverse] Running: {ffmpegPath} {arguments}");
		progressCallback?.Invoke("Reversing audio with ffmpeg...");

		var startInfo = new ProcessStartInfo
		{
			FileName = ffmpegPath,
			Arguments = arguments,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using var process = new Process { StartInfo = startInfo };
		var errorBuilder = new StringBuilder();

		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data != null)
			{
				errorBuilder.AppendLine(e.Data);
				if (e.Data.Contains("time=") || e.Data.Contains("size="))
					Logger.Info($"[ffmpeg] {e.Data}");
			}
		};

		process.Start();
		process.BeginErrorReadLine();

		var completed = await Task.Run(() => process.WaitForExit(300000));
		if (!completed)
		{
			try
			{
				process.Kill();
			}
			catch
			{
				// ignored
			}

			throw new TimeoutException("ffmpeg timed out after 5 minutes");
		}

		if (process.ExitCode != 0)
			throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}:\n{errorBuilder}");

		if (!File.Exists(outputPath))
			throw new InvalidOperationException($"ffmpeg did not create output file: {outputPath}");
	}
}
