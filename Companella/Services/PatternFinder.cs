using Companella.Models.Beatmap;

namespace Companella.Services;

/// <summary>
/// Service for detecting patterns in osu!mania beatmaps.
/// BPM is calculated from actual note/chord deltas, not from map timing points.
/// </summary>
public class PatternFinder
{
    // Pattern detection thresholds (in milliseconds)
    private const double MaxPatternIntervalMs = 250.0; // Max interval between notes in a pattern (about 60 BPM 1/4)
    private const int MinTrillNotes = 4;
    private const int MinJackNotes = 3;
    private const int MinStreamNotes = 4;
    private const int MinRollNotes = 4;
    private const double MinijackMaxIntervalMs = 100.0;
    private const int MinChordstreamNotes = 6;
    private const int MinBracketNotes = 6;
    private const double MaxRollIntervalMs = 150.0; // Rolls are fast patterns

    /// <summary>
    /// Creates a PatternFinder.
    /// </summary>
    public PatternFinder()
    {
    }

    /// <summary>
    /// Creates a PatternFinder (backwards compatible constructor, BpmCalculator is ignored).
    /// </summary>
    public PatternFinder(BpmCalculator bpmCalculator)
    {
    }

    /// <summary>
    /// Creates a PatternFinder from timing points (backwards compatible, timing points not used for BPM).
    /// </summary>
    public PatternFinder(List<TimingPoint> timingPoints)
    {
    }

    /// <summary>
    /// Creates a PatternFinder from an OsuFile (backwards compatible).
    /// </summary>
    public PatternFinder(OsuFile osuFile)
    {
    }

    /// <summary>
    /// Calculates effective BPM from note delta (assuming 1/4 notes).
    /// Formula: BPM = 15000 / delta_ms
    /// This gives the BPM at which 1/4 notes would produce this interval.
    /// </summary>
    private static double CalculateBpmFromDelta(double deltaMs)
    {
        if (deltaMs <= 0) return 0;
        // 15000 = 60000 / 4 (60000ms per minute, divided by 4 for 1/4 notes)
        return 15000.0 / deltaMs;
    }

    /// <summary>
    /// Calculates effective BPM from a list of times (chord/note events).
    /// Uses average delta between consecutive events.
    /// </summary>
    private static double CalculateBpmFromTimes(List<double> times)
    {
        if (times.Count < 2) return 0;

        var deltas = new List<double>();
        for (int i = 1; i < times.Count; i++)
        {
            var delta = times[i] - times[i - 1];
            if (delta > 0) deltas.Add(delta);
        }

        if (deltas.Count == 0) return 0;

        var averageDelta = deltas.Average();
        return CalculateBpmFromDelta(averageDelta);
    }

    /// <summary>
    /// Parses hit objects from an OsuFile's raw sections.
    /// </summary>
    public static List<HitObject> ParseHitObjects(OsuFile osuFile)
    {
        var hitObjects = new List<HitObject>();

        if (!osuFile.RawSections.TryGetValue("HitObjects", out var lines))
            return hitObjects;

        var keyCount = (int)osuFile.CircleSize;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;

            var hitObject = HitObject.Parse(line, keyCount);
            if (hitObject != null)
            {
                hitObjects.Add(hitObject);
            }
        }

        return hitObjects.OrderBy(h => h.Time).ToList();
    }

    /// <summary>
    /// Finds all patterns in a beatmap.
    /// </summary>
    public PatternAnalysisResult FindAllPatterns(OsuFile osuFile)
    {
        var hitObjects = ParseHitObjects(osuFile);
        return FindAllPatterns(hitObjects);
    }

    /// <summary>
    /// Finds all patterns in a list of hit objects.
    /// </summary>
    public PatternAnalysisResult FindAllPatterns(List<HitObject> hitObjects)
    {
        var result = new PatternAnalysisResult
        {
            TotalNotes = hitObjects.Count,
            Patterns = new Dictionary<PatternType, List<Pattern>>()
        };

        if (hitObjects.Count == 0)
            return result;

        var startTime = DateTime.UtcNow;

        try
        {
            // Group notes by time (for chord detection)
            var notesByTime = GroupNotesByTime(hitObjects);

            // Detect chords first (jumps, hands, quads)
            DetectChords(notesByTime, result);

            // Detect jacks and minijacks
            DetectJacks(hitObjects, result);

            // Detect trills
            DetectTrills(hitObjects, result);

            // Detect streams
            DetectStreams(hitObjects, notesByTime, result);

            // Detect rolls
            DetectRolls(hitObjects, result);

            // Detect jumpstreams and handstreams
            DetectChordstreams(hitObjects, notesByTime, result);

            // Detect chordjacks
            DetectChordjacks(notesByTime, result);

            // Detect jumptrills
            DetectJumptrills(notesByTime, result);

            // Detect brackets
            DetectBrackets(hitObjects, result);

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        result.AnalysisDurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
        return result;
    }

    /// <summary>
    /// Groups notes by their time (for detecting simultaneous notes).
    /// </summary>
    private Dictionary<double, List<HitObject>> GroupNotesByTime(List<HitObject> hitObjects, double toleranceMs = 2.0)
    {
        var groups = new Dictionary<double, List<HitObject>>();

        foreach (var note in hitObjects)
        {
            // Find existing group within tolerance
            var existingKey = groups.Keys.FirstOrDefault(k => Math.Abs(k - note.Time) <= toleranceMs);

            if (existingKey != default)
            {
                groups[existingKey].Add(note);
            }
            else
            {
                groups[note.Time] = new List<HitObject> { note };
            }
        }

        return groups;
    }

    /// <summary>
    /// Detects chord patterns (jumps, hands, quads).
    /// For single chords, BPM is 0 since there's no delta.
    /// </summary>
    private void DetectChords(Dictionary<double, List<HitObject>> notesByTime, PatternAnalysisResult result)
    {
        var jumps = new List<Pattern>();
        var hands = new List<Pattern>();
        var quads = new List<Pattern>();

        foreach (var kvp in notesByTime.OrderBy(k => k.Key))
        {
            var time = kvp.Key;
            var notes = kvp.Value;
            var columns = notes.Select(n => n.Column).Distinct().ToList();

            // Single chord events have no inherent BPM (BPM = 0)
            if (columns.Count >= 4)
            {
                quads.Add(CreatePatternWithBpm(PatternType.Quad, time, time, columns, notes.Count, 0));
            }
            else if (columns.Count == 3)
            {
                hands.Add(CreatePatternWithBpm(PatternType.Hand, time, time, columns, notes.Count, 0));
            }
            else if (columns.Count == 2)
            {
                jumps.Add(CreatePatternWithBpm(PatternType.Jump, time, time, columns, notes.Count, 0));
            }
        }

        if (jumps.Count > 0) result.Patterns[PatternType.Jump] = jumps;
        if (hands.Count > 0) result.Patterns[PatternType.Hand] = hands;
        if (quads.Count > 0) result.Patterns[PatternType.Quad] = quads;
    }

    /// <summary>
    /// Detects jack and minijack patterns.
    /// BPM is calculated from the average delta between jack notes.
    /// </summary>
    private void DetectJacks(List<HitObject> hitObjects, PatternAnalysisResult result)
    {
        var jacks = new List<Pattern>();
        var minijacks = new List<Pattern>();

        // Group by column
        var byColumn = hitObjects.GroupBy(h => h.Column).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Time).ToList());

        foreach (var kvp in byColumn)
        {
            var column = kvp.Key;
            var notes = kvp.Value;

            if (notes.Count < 2) continue;

            int jackStart = 0;
            int jackCount = 1;
            var jackTimes = new List<double> { notes[0].Time };

            for (int i = 1; i < notes.Count; i++)
            {
                var interval = notes[i].Time - notes[i - 1].Time;

                // Check if this continues a jack (within max pattern interval)
                if (interval <= MaxPatternIntervalMs && interval > 0)
                {
                    jackCount++;
                    jackTimes.Add(notes[i].Time);
                }
                else
                {
                    // End of jack sequence
                    if (jackCount >= MinJackNotes)
                    {
                        var bpm = CalculateBpmFromTimes(jackTimes);
                        var startTime = notes[jackStart].Time;
                        var endTime = notes[jackStart + jackCount - 1].Time;
                        jacks.Add(CreatePatternWithBpm(PatternType.Jack, startTime, endTime, new List<int> { column }, jackCount, bpm));
                    }
                    else if (jackCount == 2)
                    {
                        // Check if it's a minijack (very fast)
                        var mjInterval = notes[jackStart + 1].Time - notes[jackStart].Time;
                        if (mjInterval <= MinijackMaxIntervalMs)
                        {
                            var bpm = CalculateBpmFromDelta(mjInterval);
                            minijacks.Add(CreatePatternWithBpm(PatternType.Minijack, notes[jackStart].Time, notes[jackStart + 1].Time, new List<int> { column }, 2, bpm));
                        }
                    }

                    jackStart = i;
                    jackCount = 1;
                    jackTimes = new List<double> { notes[i].Time };
                }
            }

            // Handle remaining jack at end
            if (jackCount >= MinJackNotes)
            {
                var bpm = CalculateBpmFromTimes(jackTimes);
                var startTime = notes[jackStart].Time;
                var endTime = notes[jackStart + jackCount - 1].Time;
                jacks.Add(CreatePatternWithBpm(PatternType.Jack, startTime, endTime, new List<int> { column }, jackCount, bpm));
            }
            else if (jackCount == 2)
            {
                var mjInterval = notes[jackStart + 1].Time - notes[jackStart].Time;
                if (mjInterval <= MinijackMaxIntervalMs)
                {
                    var bpm = CalculateBpmFromDelta(mjInterval);
                    minijacks.Add(CreatePatternWithBpm(PatternType.Minijack, notes[jackStart].Time, notes[jackStart + 1].Time, new List<int> { column }, 2, bpm));
                }
            }
        }

        if (jacks.Count > 0) result.Patterns[PatternType.Jack] = jacks;
        if (minijacks.Count > 0) result.Patterns[PatternType.Minijack] = minijacks;
    }

    /// <summary>
    /// Detects trill patterns (alternating between 2 columns).
    /// BPM is calculated from note deltas within the trill.
    /// </summary>
    private void DetectTrills(List<HitObject> hitObjects, PatternAnalysisResult result)
    {
        var trills = new List<Pattern>();

        if (hitObjects.Count < MinTrillNotes) return;

        int trillStart = 0;
        int trillCount = 1;
        int col1 = hitObjects[0].Column;
        int col2 = -1;
        bool isTrill = false;
        var trillTimes = new List<double> { hitObjects[0].Time };

        for (int i = 1; i < hitObjects.Count; i++)
        {
            var currentCol = hitObjects[i].Column;
            var prevCol = hitObjects[i - 1].Column;
            var interval = hitObjects[i].Time - hitObjects[i - 1].Time;

            if (interval > MaxPatternIntervalMs || interval <= 0)
            {
                // Too slow, end trill
                if (trillCount >= MinTrillNotes && isTrill)
                {
                    var bpm = CalculateBpmFromTimes(trillTimes);
                    trills.Add(CreatePatternWithBpm(PatternType.Trill, hitObjects[trillStart].Time, hitObjects[i - 1].Time, new List<int> { col1, col2 }, trillCount, bpm));
                }
                trillStart = i;
                trillCount = 1;
                col1 = currentCol;
                col2 = -1;
                isTrill = false;
                trillTimes = new List<double> { hitObjects[i].Time };
                continue;
            }

            if (col2 == -1)
            {
                // Second note of potential trill
                if (currentCol != col1)
                {
                    col2 = currentCol;
                    trillCount++;
                    isTrill = true;
                    trillTimes.Add(hitObjects[i].Time);
                }
                else
                {
                    // Same column - not a trill
                    trillStart = i;
                    col1 = currentCol;
                    trillTimes = new List<double> { hitObjects[i].Time };
                }
            }
            else
            {
                // Check if alternating
                if ((prevCol == col1 && currentCol == col2) || (prevCol == col2 && currentCol == col1))
                {
                    trillCount++;
                    trillTimes.Add(hitObjects[i].Time);
                }
                else
                {
                    // Not alternating
                    if (trillCount >= MinTrillNotes && isTrill)
                    {
                        var bpm = CalculateBpmFromTimes(trillTimes);
                        trills.Add(CreatePatternWithBpm(PatternType.Trill, hitObjects[trillStart].Time, hitObjects[i - 1].Time, new List<int> { col1, col2 }, trillCount, bpm));
                    }
                    trillStart = i;
                    trillCount = 1;
                    col1 = currentCol;
                    col2 = -1;
                    isTrill = false;
                    trillTimes = new List<double> { hitObjects[i].Time };
                }
            }
        }

        // Handle remaining trill
        if (trillCount >= MinTrillNotes && isTrill)
        {
            var bpm = CalculateBpmFromTimes(trillTimes);
            trills.Add(CreatePatternWithBpm(PatternType.Trill, hitObjects[trillStart].Time, hitObjects[^1].Time, new List<int> { col1, col2 }, trillCount, bpm));
        }

        if (trills.Count > 0) result.Patterns[PatternType.Trill] = trills;
    }

    /// <summary>
    /// Detects stream patterns (consistent intervals, no repeated columns).
    /// BPM is calculated from note deltas within the stream.
    /// </summary>
    private void DetectStreams(List<HitObject> hitObjects, Dictionary<double, List<HitObject>> notesByTime, PatternAnalysisResult result)
    {
        var streams = new List<Pattern>();

        // Get single notes only (no chords)
        var singleNotes = notesByTime
            .Where(kvp => kvp.Value.Select(n => n.Column).Distinct().Count() == 1)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => kvp.Value.First())
            .ToList();

        if (singleNotes.Count < MinStreamNotes) return;

        int streamStart = 0;
        int streamCount = 1;
        int lastColumn = singleNotes[0].Column;
        var columns = new HashSet<int> { lastColumn };
        var streamTimes = new List<double> { singleNotes[0].Time };

        for (int i = 1; i < singleNotes.Count; i++)
        {
            var current = singleNotes[i];
            var prev = singleNotes[i - 1];
            var interval = current.Time - prev.Time;

            // Check: different column, consistent interval
            if (current.Column != lastColumn && interval <= MaxPatternIntervalMs && interval > 0)
            {
                streamCount++;
                columns.Add(current.Column);
                lastColumn = current.Column;
                streamTimes.Add(current.Time);
            }
            else
            {
                // End of stream
                if (streamCount >= MinStreamNotes)
                {
                    var bpm = CalculateBpmFromTimes(streamTimes);
                    streams.Add(CreatePatternWithBpm(PatternType.Stream, singleNotes[streamStart].Time, singleNotes[i - 1].Time, columns.ToList(), streamCount, bpm));
                }
                streamStart = i;
                streamCount = 1;
                lastColumn = current.Column;
                columns = new HashSet<int> { lastColumn };
                streamTimes = new List<double> { current.Time };
            }
        }

        // Handle remaining stream
        if (streamCount >= MinStreamNotes)
        {
            var bpm = CalculateBpmFromTimes(streamTimes);
            streams.Add(CreatePatternWithBpm(PatternType.Stream, singleNotes[streamStart].Time, singleNotes[^1].Time, columns.ToList(), streamCount, bpm));
        }

        if (streams.Count > 0) result.Patterns[PatternType.Stream] = streams;
    }

    /// <summary>
    /// Detects roll patterns (ascending/descending column sequences).
    /// BPM is calculated from note deltas within the roll.
    /// </summary>
    private void DetectRolls(List<HitObject> hitObjects, PatternAnalysisResult result)
    {
        var rolls = new List<Pattern>();

        if (hitObjects.Count < MinRollNotes) return;

        int rollStart = 0;
        int rollCount = 1;
        int direction = 0; // 1 = ascending, -1 = descending, 0 = unknown
        var columns = new HashSet<int> { hitObjects[0].Column };
        var rollTimes = new List<double> { hitObjects[0].Time };

        for (int i = 1; i < hitObjects.Count; i++)
        {
            var current = hitObjects[i];
            var prev = hitObjects[i - 1];
            var interval = current.Time - prev.Time;

            var colDiff = current.Column - prev.Column;

            if (interval <= MaxRollIntervalMs && interval > 0 && colDiff != 0)
            {
                var currentDirection = colDiff > 0 ? 1 : -1;

                if (direction == 0)
                {
                    direction = currentDirection;
                    rollCount++;
                    columns.Add(current.Column);
                    rollTimes.Add(current.Time);
                }
                else if (currentDirection == direction || (Math.Abs(colDiff) == 1))
                {
                    rollCount++;
                    columns.Add(current.Column);
                    rollTimes.Add(current.Time);
                }
                else
                {
                    // Direction changed
                    if (rollCount >= MinRollNotes)
                    {
                        var bpm = CalculateBpmFromTimes(rollTimes);
                        rolls.Add(CreatePatternWithBpm(PatternType.Roll, hitObjects[rollStart].Time, hitObjects[i - 1].Time, columns.ToList(), rollCount, bpm));
                    }
                    rollStart = i;
                    rollCount = 1;
                    direction = 0;
                    columns = new HashSet<int> { current.Column };
                    rollTimes = new List<double> { current.Time };
                }
            }
            else
            {
                // End of roll
                if (rollCount >= MinRollNotes)
                {
                    var bpm = CalculateBpmFromTimes(rollTimes);
                    rolls.Add(CreatePatternWithBpm(PatternType.Roll, hitObjects[rollStart].Time, hitObjects[i - 1].Time, columns.ToList(), rollCount, bpm));
                }
                rollStart = i;
                rollCount = 1;
                direction = 0;
                columns = new HashSet<int> { current.Column };
                rollTimes = new List<double> { current.Time };
            }
        }

        // Handle remaining roll
        if (rollCount >= MinRollNotes)
        {
            var bpm = CalculateBpmFromTimes(rollTimes);
            rolls.Add(CreatePatternWithBpm(PatternType.Roll, hitObjects[rollStart].Time, hitObjects[^1].Time, columns.ToList(), rollCount, bpm));
        }

        if (rolls.Count > 0) result.Patterns[PatternType.Roll] = rolls;
    }

    /// <summary>
    /// Detects jumpstream and handstream patterns.
    /// BPM is calculated from chord event deltas.
    /// </summary>
    private void DetectChordstreams(List<HitObject> hitObjects, Dictionary<double, List<HitObject>> notesByTime, PatternAnalysisResult result)
    {
        var jumpstreams = new List<Pattern>();
        var handstreams = new List<Pattern>();

        var sortedTimes = notesByTime.Keys.OrderBy(t => t).ToList();
        if (sortedTimes.Count < MinChordstreamNotes) return;

        int csStart = 0;
        int noteCount = 0;
        int jumpCount = 0;
        int handCount = 0;
        var columns = new HashSet<int>();
        var eventTimes = new List<double>();

        for (int i = 0; i < sortedTimes.Count; i++)
        {
            var time = sortedTimes[i];
            var notes = notesByTime[time];
            var chordSize = notes.Select(n => n.Column).Distinct().Count();

            double interval = 0;
            if (i > 0)
            {
                interval = time - sortedTimes[i - 1];
            }

            if (interval <= MaxPatternIntervalMs || i == 0)
            {
                noteCount += chordSize;
                eventTimes.Add(time);
                foreach (var note in notes) columns.Add(note.Column);

                if (chordSize == 2) jumpCount++;
                else if (chordSize >= 3) handCount++;
            }
            else
            {
                // End of section, check if it's a chordstream
                if (noteCount >= MinChordstreamNotes)
                {
                    var bpm = CalculateBpmFromTimes(eventTimes);
                    if (handCount > 0)
                    {
                        handstreams.Add(CreatePatternWithBpm(PatternType.Handstream, sortedTimes[csStart], sortedTimes[i - 1], columns.ToList(), noteCount, bpm));
                    }
                    else if (jumpCount > 0)
                    {
                        jumpstreams.Add(CreatePatternWithBpm(PatternType.Jumpstream, sortedTimes[csStart], sortedTimes[i - 1], columns.ToList(), noteCount, bpm));
                    }
                }

                csStart = i;
                noteCount = chordSize;
                jumpCount = chordSize == 2 ? 1 : 0;
                handCount = chordSize >= 3 ? 1 : 0;
                columns = new HashSet<int>(notes.Select(n => n.Column));
                eventTimes = new List<double> { time };
            }
        }

        // Handle remaining
        if (noteCount >= MinChordstreamNotes)
        {
            var bpm = CalculateBpmFromTimes(eventTimes);
            if (handCount > 0)
            {
                handstreams.Add(CreatePatternWithBpm(PatternType.Handstream, sortedTimes[csStart], sortedTimes[^1], columns.ToList(), noteCount, bpm));
            }
            else if (jumpCount > 0)
            {
                jumpstreams.Add(CreatePatternWithBpm(PatternType.Jumpstream, sortedTimes[csStart], sortedTimes[^1], columns.ToList(), noteCount, bpm));
            }
        }

        if (jumpstreams.Count > 0) result.Patterns[PatternType.Jumpstream] = jumpstreams;
        if (handstreams.Count > 0) result.Patterns[PatternType.Handstream] = handstreams;
    }

    /// <summary>
    /// Detects chordjack patterns (consecutive chords in same positions).
    /// BPM is calculated from chord event deltas.
    /// </summary>
    private void DetectChordjacks(Dictionary<double, List<HitObject>> notesByTime, PatternAnalysisResult result)
    {
        var chordjacks = new List<Pattern>();

        var chordTimes = notesByTime
            .Where(kvp => kvp.Value.Select(n => n.Column).Distinct().Count() >= 2)
            .OrderBy(kvp => kvp.Key)
            .ToList();

        if (chordTimes.Count < 3) return;

        int cjStart = 0;
        int cjCount = 1;
        var columns = new HashSet<int>(chordTimes[0].Value.Select(n => n.Column));
        int noteCount = chordTimes[0].Value.Count;
        var eventTimes = new List<double> { chordTimes[0].Key };

        for (int i = 1; i < chordTimes.Count; i++)
        {
            var time = chordTimes[i].Key;
            var prevTime = chordTimes[i - 1].Key;
            var interval = time - prevTime;

            var currentCols = chordTimes[i].Value.Select(n => n.Column).ToHashSet();
            var prevCols = chordTimes[i - 1].Value.Select(n => n.Column).ToHashSet();

            // Check if there's column overlap (shared columns = jack-like)
            var overlap = currentCols.Intersect(prevCols).Count();

            if (interval <= MaxPatternIntervalMs && overlap > 0)
            {
                cjCount++;
                foreach (var col in currentCols) columns.Add(col);
                noteCount += chordTimes[i].Value.Count;
                eventTimes.Add(time);
            }
            else
            {
                if (cjCount >= 3)
                {
                    var bpm = CalculateBpmFromTimes(eventTimes);
                    chordjacks.Add(CreatePatternWithBpm(PatternType.Chordjack, chordTimes[cjStart].Key, chordTimes[i - 1].Key, columns.ToList(), noteCount, bpm));
                }
                cjStart = i;
                cjCount = 1;
                columns = new HashSet<int>(currentCols);
                noteCount = chordTimes[i].Value.Count;
                eventTimes = new List<double> { time };
            }
        }

        if (cjCount >= 3)
        {
            var bpm = CalculateBpmFromTimes(eventTimes);
            chordjacks.Add(CreatePatternWithBpm(PatternType.Chordjack, chordTimes[cjStart].Key, chordTimes[^1].Key, columns.ToList(), noteCount, bpm));
        }

        if (chordjacks.Count > 0) result.Patterns[PatternType.Chordjack] = chordjacks;
    }

    /// <summary>
    /// Detects jumptrill patterns (alternating jumps).
    /// BPM is calculated from jump event deltas.
    /// </summary>
    private void DetectJumptrills(Dictionary<double, List<HitObject>> notesByTime, PatternAnalysisResult result)
    {
        var jumptrills = new List<Pattern>();

        var jumpTimes = notesByTime
            .Where(kvp => kvp.Value.Select(n => n.Column).Distinct().Count() == 2)
            .OrderBy(kvp => kvp.Key)
            .ToList();

        if (jumpTimes.Count < 4) return;

        int jtStart = 0;
        int jtCount = 1;
        HashSet<int>? pattern1 = null;
        HashSet<int>? pattern2 = null;
        var allColumns = new HashSet<int>();
        int noteCount = 0;
        var eventTimes = new List<double>();

        for (int i = 0; i < jumpTimes.Count; i++)
        {
            var time = jumpTimes[i].Key;
            var cols = jumpTimes[i].Value.Select(n => n.Column).ToHashSet();

            if (i == 0)
            {
                pattern1 = cols;
                noteCount = jumpTimes[i].Value.Count;
                foreach (var c in cols) allColumns.Add(c);
                eventTimes.Add(time);
                continue;
            }

            var interval = time - jumpTimes[i - 1].Key;

            if (interval > MaxPatternIntervalMs)
            {
                // End of jumptrill
                if (jtCount >= 4)
                {
                    var bpm = CalculateBpmFromTimes(eventTimes);
                    jumptrills.Add(CreatePatternWithBpm(PatternType.Jumptrill, jumpTimes[jtStart].Key, jumpTimes[i - 1].Key, allColumns.ToList(), noteCount, bpm));
                }
                jtStart = i;
                jtCount = 1;
                pattern1 = cols;
                pattern2 = null;
                allColumns = new HashSet<int>(cols);
                noteCount = jumpTimes[i].Value.Count;
                eventTimes = new List<double> { time };
                continue;
            }

            if (pattern2 == null)
            {
                if (!cols.SetEquals(pattern1!))
                {
                    pattern2 = cols;
                    jtCount++;
                    foreach (var c in cols) allColumns.Add(c);
                    noteCount += jumpTimes[i].Value.Count;
                    eventTimes.Add(time);
                }
                else
                {
                    // Same pattern - not a jumptrill
                    jtStart = i;
                    jtCount = 1;
                    pattern1 = cols;
                    eventTimes = new List<double> { time };
                }
            }
            else
            {
                // Check if alternating
                var prevCols = jumpTimes[i - 1].Value.Select(n => n.Column).ToHashSet();
                var expectedCols = prevCols.SetEquals(pattern1!) ? pattern2 : pattern1;

                if (cols.SetEquals(expectedCols))
                {
                    jtCount++;
                    noteCount += jumpTimes[i].Value.Count;
                    eventTimes.Add(time);
                }
                else
                {
                    // Not alternating
                    if (jtCount >= 4)
                    {
                        var bpm = CalculateBpmFromTimes(eventTimes);
                        jumptrills.Add(CreatePatternWithBpm(PatternType.Jumptrill, jumpTimes[jtStart].Key, jumpTimes[i - 1].Key, allColumns.ToList(), noteCount, bpm));
                    }
                    jtStart = i;
                    jtCount = 1;
                    pattern1 = cols;
                    pattern2 = null;
                    allColumns = new HashSet<int>(cols);
                    noteCount = jumpTimes[i].Value.Count;
                    eventTimes = new List<double> { time };
                }
            }
        }

        if (jtCount >= 4)
        {
            var bpm = CalculateBpmFromTimes(eventTimes);
            jumptrills.Add(CreatePatternWithBpm(PatternType.Jumptrill, jumpTimes[jtStart].Key, jumpTimes[^1].Key, allColumns.ToList(), noteCount, bpm));
        }

        if (jumptrills.Count > 0) result.Patterns[PatternType.Jumptrill] = jumptrills;
    }

    /// <summary>
    /// Detects bracket patterns (multiple adjacent trills).
    /// BPM is calculated from note deltas.
    /// </summary>
    private void DetectBrackets(List<HitObject> hitObjects, PatternAnalysisResult result)
    {
        var brackets = new List<Pattern>();

        if (hitObjects.Count < MinBracketNotes) return;

        // Look for sections where notes rapidly alternate between adjacent column pairs
        int bracketStart = 0;
        int bracketCount = 1;
        var columns = new HashSet<int> { hitObjects[0].Column };
        var bracketTimes = new List<double> { hitObjects[0].Time };

        for (int i = 1; i < hitObjects.Count; i++)
        {
            var current = hitObjects[i];
            var prev = hitObjects[i - 1];
            var interval = current.Time - prev.Time;

            var colDiff = Math.Abs(current.Column - prev.Column);

            // Brackets are fast patterns with adjacent column movement
            if (interval <= MaxRollIntervalMs && interval > 0 && colDiff <= 2 && colDiff >= 1)
            {
                bracketCount++;
                columns.Add(current.Column);
                bracketTimes.Add(current.Time);
            }
            else
            {
                if (bracketCount >= MinBracketNotes && columns.Count >= 3)
                {
                    var bpm = CalculateBpmFromTimes(bracketTimes);
                    brackets.Add(CreatePatternWithBpm(PatternType.Bracket, hitObjects[bracketStart].Time, hitObjects[i - 1].Time, columns.ToList(), bracketCount, bpm));
                }
                bracketStart = i;
                bracketCount = 1;
                columns = new HashSet<int> { current.Column };
                bracketTimes = new List<double> { current.Time };
            }
        }

        if (bracketCount >= MinBracketNotes && columns.Count >= 3)
        {
            var bpm = CalculateBpmFromTimes(bracketTimes);
            brackets.Add(CreatePatternWithBpm(PatternType.Bracket, hitObjects[bracketStart].Time, hitObjects[^1].Time, columns.ToList(), bracketCount, bpm));
        }

        if (brackets.Count > 0) result.Patterns[PatternType.Bracket] = brackets;
    }

    /// <summary>
    /// Creates a Pattern object with the specified BPM.
    /// </summary>
    private static Pattern CreatePatternWithBpm(PatternType type, double startTime, double endTime, List<int> columns, int noteCount, double bpm)
    {
        return new Pattern
        {
            Type = type,
            StartTime = startTime,
            EndTime = endTime,
            Bpm = bpm,
            Columns = columns,
            NoteCount = noteCount
        };
    }
}
