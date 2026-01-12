using Companella.Models.Beatmap;
using Companella.Services.Beatmap;

namespace Companella.Services.Analysis;

/// <summary>
/// Service for calculating YAVSRG/Interlude difficulty rating from chart data.
/// Implements the exact difficulty calculation algorithm from YAVSRG's Calculator.Difficulty module.
/// 
/// This is a separate system from MSD - it calculates difficulty directly from the chart's
/// notes and timing points using YAVSRG's own algorithm.
/// </summary>
public class InterludeDifficultyService
{
    // Constants from YAVSRG's Difficulty.fs
    private const float CurvePower = 0.6f;
    private const float CurveScale = 0.4056f;
    private const float MostImportantNotes = 2500.0f;

    // Constants from YAVSRG's Notes.fs
    private const float JackCurveCutoff = 230.0f;
    private const float StreamCurveCutoff = 10.0f;
    private const float StreamCurveCutoff2 = 10.0f;
    private const float OhtNerf = 3.0f;
    private const float StreamScale = 6.0f;
    private const float StreamPow = 0.5f;

    // Constants from YAVSRG's Strain.fs
    private const float StrainScale = 0.01626f;
    private const float StrainTimeCap = 200.0f; // ms/rate

    /// <summary>
    /// Weighting curve function from YAVSRG.
    /// x = position through the top 2500 from 0.0 - 1.0
    /// Values below the top 2500 always have x = 0.0
    /// </summary>
    private static float WeightingCurve(float x)
    {
        return 0.002f + (float)Math.Pow(x, 4.0);
    }

    /// <summary>
    /// Calculates YAVSRG difficulty using the weighted_overall_difficulty algorithm.
    /// This is the exact implementation from YAVSRG's Calculator.Difficulty module.
    /// </summary>
    private static float WeightedOverallDifficulty(IEnumerable<float> data)
    {
        var dataArray = data.Where(x => x > 0.0f).OrderBy(x => x).ToArray();
        var length = (float)dataArray.Length;

        if (dataArray.Length == 0)
            return 0.0f;

        float weight = 0.0f;
        float total = 0.0f;

        for (int i = 0; i < dataArray.Length; i++)
        {
            // Calculate position through the top 2500 notes
            var position = (i + MostImportantNotes - length) / MostImportantNotes;
            var x = Math.Max(0.0f, position);
            
            var w = WeightingCurve(x);
            weight += w;
            total += dataArray[i] * w;
        }

        if (weight <= 0.0f)
            return 0.0f;

        // Final transform: Power and rescale to YAVSRG's difficulty scale
        var weightedAverage = total / weight;
        var result = (float)Math.Pow(weightedAverage, CurvePower) * CurveScale;

        return float.IsFinite(result) ? result : 0.0f;
    }

    /// <summary>
    /// Converts ms difference between notes (in the same column) into its equivalent BPM for jacks.
    /// From YAVSRG's Notes.fs
    /// </summary>
    private static float MsToJackBpm(float deltaMs)
    {
        return Math.Min(15000.0f / deltaMs, JackCurveCutoff);
    }

    /// <summary>
    /// Converts ms difference between notes (in adjacent columns) into its equivalent BPM for streams.
    /// From YAVSRG's Notes.fs
    /// </summary>
    private static float MsToStreamBpm(float deltaMs)
    {
        var result = 300.0f / (0.02f * deltaMs) - 300.0f / (float)Math.Pow(0.02f * deltaMs, StreamCurveCutoff) / StreamCurveCutoff2;
        return Math.Max(0.0f, result);
    }

    /// <summary>
    /// Jack compensation function from YAVSRG.
    /// Uses the ratio between jack and stream spacing to determine a multiplier between 0.0 and 1.0.
    /// </summary>
    private static float JackCompensation(float jackDelta, float streamDelta)
    {
        if (streamDelta <= 0.0f)
            return 1.0f;

        var ratio = jackDelta / streamDelta;
        var logRatio = (float)Math.Log(ratio, 2.0);
        return Math.Min(1.0f, (float)Math.Sqrt(Math.Max(0.0f, logRatio)));
    }

    /// <summary>
    /// Calculates total note difficulty from J, SL, SR components.
    /// From YAVSRG's Notes.fs
    /// </summary>
    private static float CalculateNoteTotal(float j, float sl, float sr)
    {
        return (float)Math.Pow(
            Math.Pow(StreamScale * (float)Math.Pow(sl, StreamPow), OhtNerf) +
            Math.Pow(StreamScale * (float)Math.Pow(sr, StreamPow), OhtNerf) +
            Math.Pow(j, OhtNerf),
            1.0f / OhtNerf
        );
    }

    /// <summary>
    /// Strain decay function from YAVSRG's Strain.fs
    /// </summary>
    private static float StrainFunc(float halfLifeMs, float currentValue, float input, float deltaMs)
    {
        var decayRate = (float)Math.Log(0.5) / halfLifeMs;
        var decay = (float)Math.Exp(decayRate * Math.Min(StrainTimeCap, deltaMs));
        var timeCapDecay = deltaMs > StrainTimeCap 
            ? (float)Math.Exp(decayRate * (deltaMs - StrainTimeCap)) 
            : 1.0f;
        
        var a = currentValue * timeCapDecay;
        var b = input * input * StrainScale;
        return b - (b - a) * decay;
    }

    /// <summary>
    /// Calculates YAVSRG/Interlude difficulty rating from chart data.
    /// This implements the full YAVSRG difficulty calculation pipeline:
    /// 1. Calculate note difficulties (J, SL, SR)
    /// 2. Calculate finger strains
    /// 3. Apply weighted overall difficulty algorithm
    /// </summary>
    /// <param name="hitObjects">List of hit objects (notes) from the chart.</param>
    /// <param name="timingPoints">List of timing points for BPM calculation.</param>
    /// <param name="rate">Music rate multiplier (default 1.0).</param>
    /// <param name="keyCount">Number of keys/columns (default 4 for 4K).</param>
    /// <returns>The calculated YAVSRG difficulty rating.</returns>
    public double CalculateDifficulty(
        List<HitObject> hitObjects, 
        List<TimingPoint> timingPoints, 
        float rate = 1.0f,
        int keyCount = 4)
    {
        if (hitObjects == null || hitObjects.Count == 0)
            return 0.0;

        if (timingPoints == null || timingPoints.Count == 0)
            return 0.0;

        // Group hit objects by time to create note rows
        var noteRows = hitObjects
            .GroupBy(h => h.Time)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Time = (float)g.Key,
                Notes = g.Select(h => new { h.Column, h.Type }).ToList()
            })
            .ToList();

        if (noteRows.Count == 0)
            return 0.0;

        // Calculate BPM at each timing point for rate adjustment
        var bpmCalculator = new BpmCalculator(timingPoints);

        // Calculate note difficulties and strains
        var lastNoteInColumn = new float[keyCount];
        var strainValues = new float[keyCount];
        var strainDataPoints = new List<float>();

        // Hand split for 4K: keys 0-1 are left hand, 2-3 are right hand
        var handSplit = keyCount / 2;

        foreach (var row in noteRows)
        {
            var time = row.Time / rate; // Apply rate
            var bpm = (float)bpmCalculator.GetBpmAtTime(row.Time);
            var beatLength = bpm > 0 ? 60000.0f / bpm : 0.0f;
            
            // Note difficulty for this row
            var noteDifficulties = new float[keyCount];
            var rowStrains = new float[keyCount];

            for (int k = 0; k < keyCount; k++)
            {
                var hasNote = row.Notes.Any(n => n.Column == k && 
                    (n.Type == HitObjectType.Circle || n.Type == HitObjectType.Hold));

                if (hasNote)
                {
                    // Calculate jack difficulty (same column)
                    var jackDelta = time - lastNoteInColumn[k];
                    var j = jackDelta > 0 ? MsToJackBpm(jackDelta) : 0.0f;

                    // Calculate stream difficulty (adjacent columns on same hand)
                    var handLo = k < handSplit ? 0 : handSplit;
                    var handHi = k < handSplit ? handSplit - 1 : keyCount - 1;

                    float sl = 0.0f; // stream left
                    float sr = 0.0f; // stream right

                    for (int handK = handLo; handK <= handHi; handK++)
                    {
                        if (handK != k)
                        {
                            var trillDelta = time - lastNoteInColumn[handK];
                            if (trillDelta > 0)
                            {
                                var trillV = MsToStreamBpm(trillDelta) * JackCompensation(jackDelta, trillDelta);
                                if (handK < k)
                                    sl = Math.Max(sl, trillV);
                                else
                                    sr = Math.Max(sr, trillV);
                            }
                        }
                    }

                    // Calculate total note difficulty
                    noteDifficulties[k] = CalculateNoteTotal(j, sl, sr);

                    // Calculate strain
                    var input = noteDifficulties[k];
                    var delta = Math.Max(0.0f, jackDelta);
                    
                    // Use burst strain (half-life 1575ms)
                    strainValues[k] = StrainFunc(1575.0f, strainValues[k], input, delta);
                    rowStrains[k] = strainValues[k];

                    lastNoteInColumn[k] = time;
                }
            }

            // Collect strain values for weighted difficulty calculation
            foreach (var strain in rowStrains)
            {
                if (strain > 0.0f)
                    strainDataPoints.Add(strain);
            }
        }

        // Apply YAVSRG's weighted overall difficulty algorithm
        var result = WeightedOverallDifficulty(strainDataPoints);
        return result;
    }

    /// <summary>
    /// Calculates YAVSRG difficulty from an OsuFile.
    /// Convenience method that extracts hit objects and timing points automatically.
    /// </summary>
    /// <param name="osuFile">The parsed osu! file.</param>
    /// <param name="rate">Music rate multiplier (default 1.0).</param>
    /// <returns>The calculated YAVSRG difficulty rating.</returns>
    public double CalculateDifficulty(OsuFile osuFile, float rate = 1.0f)
    {
        if (osuFile == null)
            return 0.0;

        // Extract hit objects from raw sections
        var hitObjects = new List<HitObject>();
        
        // Try to get hit objects from raw sections (if file was already parsed)
        if (osuFile.RawSections.TryGetValue("HitObjects", out var hitObjectLines))
        {
            foreach (var line in hitObjectLines)
            {
                // Skip comments
                if (line.Trim().StartsWith("//"))
                    continue;
                    
                var hitObj = HitObject.Parse(line, 4);
                if (hitObj != null)
                    hitObjects.Add(hitObj);
            }
        }
        else
        {
            // If not in raw sections, re-parse the file
            var parser = new OsuFileParser();
            var fullOsuFile = parser.Parse(osuFile.FilePath);
            
            if (fullOsuFile.RawSections.TryGetValue("HitObjects", out hitObjectLines))
            {
                foreach (var line in hitObjectLines)
                {
                    if (line.Trim().StartsWith("//"))
                        continue;
                        
                    var hitObj = HitObject.Parse(line, 4);
                    if (hitObj != null)
                        hitObjects.Add(hitObj);
                }
            }
        }

        if (hitObjects.Count == 0)
            return 0.0;

        return CalculateDifficulty(hitObjects, osuFile.TimingPoints, rate, 4);
    }
}
