using OsuMappingHelper.Services;

namespace OsuMappingHelper.Models;

/// <summary>
/// Represents the judgement result for a hit in osu!mania.
/// </summary>
public enum ManiaJudgement
{
    /// <summary>
    /// Perfect hit (MAX/300g in some skins).
    /// </summary>
    Max300 = 0,
    
    /// <summary>
    /// Great hit (300).
    /// </summary>
    Hit300 = 1,
    
    /// <summary>
    /// Good hit (200).
    /// </summary>
    Hit200 = 2,
    
    /// <summary>
    /// OK hit (100).
    /// </summary>
    Hit100 = 3,
    
    /// <summary>
    /// Bad hit (50).
    /// </summary>
    Hit50 = 4,
    
    /// <summary>
    /// Missed note.
    /// </summary>
    Miss = 5
}

/// <summary>
/// Represents the timing deviation for a single hit object.
/// </summary>
public class TimingDeviation
{
    /// <summary>
    /// The time when the note should have been hit (in milliseconds).
    /// </summary>
    public double ExpectedTime { get; set; }
    
    /// <summary>
    /// The time when the player actually hit the note (in milliseconds).
    /// </summary>
    public double ActualTime { get; set; }
    
    /// <summary>
    /// The timing deviation in milliseconds.
    /// Negative = early, Positive = late.
    /// </summary>
    public double Deviation => ActualTime - ExpectedTime;
    
    /// <summary>
    /// The column/key index (0-based).
    /// </summary>
    public int Column { get; set; }
    
    /// <summary>
    /// The judgement result for this hit.
    /// </summary>
    public ManiaJudgement Judgement { get; set; }
    
    /// <summary>
    /// Whether this deviation is for the head of a hold note.
    /// </summary>
    public bool IsHoldHead { get; set; }
    
    /// <summary>
    /// Whether this deviation is for the tail of a hold note.
    /// </summary>
    public bool IsHoldTail { get; set; }
    
    /// <summary>
    /// Whether this note had NO keypress matched to it at all.
    /// When true, this note should ALWAYS be a Miss regardless of OD/system settings.
    /// When false, the note was hit and judgement should be recalculated based on deviation.
    /// </summary>
    public bool WasNeverHit { get; set; }
    
    /// <summary>
    /// For LNs: the tail/release deviation in milliseconds.
    /// Null for regular notes.
    /// </summary>
    public double? TailDeviation { get; set; }
    
    /// <summary>
    /// Whether this is an LN (has both head and tail deviations).
    /// </summary>
    public bool IsLongNote => TailDeviation.HasValue;
    
    /// <summary>
    /// Creates a new TimingDeviation instance.
    /// </summary>
    public TimingDeviation(double expectedTime, double actualTime, int column, ManiaJudgement judgement)
    {
        ExpectedTime = expectedTime;
        ActualTime = actualTime;
        Column = column;
        Judgement = judgement;
    }
    
    /// <summary>
    /// Gets the judgement based on the absolute deviation value.
    /// Uses osu!mania OD8 timing windows by default.
    /// For regular notes only - use GetLNJudgementFromDeviations for LNs.
    /// </summary>
    public static ManiaJudgement GetJudgementFromDeviation(double absDeviation, double od = 8.0)
    {
        // osu!mania timing windows formula:
        // MAX/300g: 16ms (fixed)
        // 300: 64 - 3 * OD
        // 200: 97 - 3 * OD
        // 100: 127 - 3 * OD
        // 50: 151 - 3 * OD
        // Miss: 188 - 3 * OD
        
        double window300g = 16;
        double window300 = 64 - 3 * od;
        double window200 = 97 - 3 * od;
        double window100 = 127 - 3 * od;
        double window50 = 151 - 3 * od;
        
        if (absDeviation <= window300g) return ManiaJudgement.Max300;
        if (absDeviation <= window300) return ManiaJudgement.Hit300;
        if (absDeviation <= window200) return ManiaJudgement.Hit200;
        if (absDeviation <= window100) return ManiaJudgement.Hit100;
        if (absDeviation <= window50) return ManiaJudgement.Hit50;
        return ManiaJudgement.Miss;
    }
    
    /// <summary>
    /// Gets the combined LN judgement based on head and tail deviations.
    /// Based on Mania-Replay-Master's isLNJudgedWith logic for non-ScoreV2.
    /// </summary>
    public static ManiaJudgement GetLNJudgementFromDeviations(double headDeviation, double tailDeviation, double od = 8.0)
    {
        double startDiff = Math.Abs(headDeviation);
        double endDiff = Math.Abs(tailDeviation);
        double totalDiff = startDiff + endDiff;
        
        // Windows for each judgement level
        double windowMax = 16.0;
        double window300 = 64.0 - 3.0 * od;
        double window200 = 97.0 - 3.0 * od;
        double window100 = 127.0 - 3.0 * od;
        
        // Check from best to worst (with rate multipliers from Mania-Replay-Master)
        // isLNJudgedWith: startDiff <= window*rate AND totalDiff <= window*rate*2
        if (IsLNJudgedWith(startDiff, totalDiff, windowMax, 1.2))
            return ManiaJudgement.Max300;
        if (IsLNJudgedWith(startDiff, totalDiff, window300, 1.1))
            return ManiaJudgement.Hit300;
        if (IsLNJudgedWith(startDiff, totalDiff, window200, 1.0))
            return ManiaJudgement.Hit200;
        if (IsLNJudgedWith(startDiff, totalDiff, window100, 1.0))
            return ManiaJudgement.Hit100;
        
        return ManiaJudgement.Hit50;
    }
    
    /// <summary>
    /// Helper for LN judgement calculation.
    /// Based on Mania-Replay-Master's isLNJudgedWith function.
    /// </summary>
    private static bool IsLNJudgedWith(double startDiff, double totalDiff, double judgementWindow, double rate)
    {
        double threshold = judgementWindow * rate;
        return startDiff <= threshold && totalDiff <= threshold * 2;
    }
    
    public override string ToString()
    {
        var direction = Deviation < 0 ? "early" : "late";
        return $"[{ExpectedTime:F0}ms] Col{Column}: {Math.Abs(Deviation):F1}ms {direction} ({Judgement})";
    }
}

/// <summary>
/// Contains the complete timing analysis results for a replay.
/// </summary>
public class TimingAnalysisResult
{
    /// <summary>
    /// List of all timing deviations for each hit.
    /// </summary>
    public List<TimingDeviation> Deviations { get; set; } = new();
    
    /// <summary>
    /// The total duration of the map in milliseconds.
    /// </summary>
    public double MapDuration { get; set; }
    
    /// <summary>
    /// The Unstable Rate (UR) - standard deviation * 10.
    /// </summary>
    public double UnstableRate { get; set; }
    
    /// <summary>
    /// The mean (average) deviation in milliseconds.
    /// Negative = tends early, Positive = tends late.
    /// </summary>
    public double MeanDeviation { get; set; }
    
    /// <summary>
    /// The beatmap path this analysis is for.
    /// </summary>
    public string BeatmapPath { get; set; } = string.Empty;
    
    /// <summary>
    /// The rate multiplier used (1.0 = normal, 1.5 = DT, 0.75 = HT).
    /// </summary>
    public float Rate { get; set; } = 1.0f;
    
    /// <summary>
    /// The Overall Difficulty of the beatmap.
    /// </summary>
    public double OverallDifficulty { get; set; } = 8.0;
    
    /// <summary>
    /// Whether mirror mod was active.
    /// </summary>
    public bool HasMirror { get; set; }
    
    /// <summary>
    /// The original key events from the replay (for re-analysis).
    /// </summary>
    public List<ManiaKeyEvent>? OriginalKeyEvents { get; set; }
    
    /// <summary>
    /// Whether the analysis was successful.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if the analysis failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Calculates statistics from the deviations list.
    /// </summary>
    public void CalculateStatistics()
    {
        if (Deviations.Count == 0)
        {
            UnstableRate = 0;
            MeanDeviation = 0;
            return;
        }
        
        // Calculate mean
        MeanDeviation = Deviations.Average(d => d.Deviation);
        
        // Calculate standard deviation
        double sumSquaredDiff = Deviations.Sum(d => Math.Pow(d.Deviation - MeanDeviation, 2));
        double stdDev = Math.Sqrt(sumSquaredDiff / Deviations.Count);
        
        // UR = stddev * 10
        UnstableRate = stdDev * 10;
    }
}

