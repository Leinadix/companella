using Companella.Models.Application;

namespace Companella.Services.Analysis;

/// <summary>
/// Result of manip factor calculation.
/// </summary>
public class ManipFactorResult
{
	/// <summary>
	/// Combined manip factor (0.0 to 1.0+ scale, higher = more manipulation).
	/// </summary>
	public double ManipFactor { get; set; }

	/// <summary>
	/// Manip factor for left hand columns only.
	/// </summary>
	public double LeftHandManip { get; set; }

	/// <summary>
	/// Manip factor for right hand columns only.
	/// </summary>
	public double RightHandManip { get; set; }

	/// <summary>
	/// Whether the calculation was successful (enough data).
	/// </summary>
	public bool Success { get; set; }

	/// <summary>
	/// Number of deviation samples used in calculation.
	/// </summary>
	public int SampleCount { get; set; }
}

/// <summary>
/// Calculates the Manip Factor metric based on Etterna's algorithm.
/// Measures timing deviations between adjacent column hits to detect manipulative play.
/// </summary>
public class ManipFactorCalculator
{
	/// <summary>
	/// Calculates the manip factor from timing deviations.
	/// </summary>
	/// <param name="deviations">List of timing deviations from replay analysis.</param>
	/// <param name="keyCount">Number of keys/columns in the map.</param>
	/// <returns>ManipFactorResult with combined and per-hand manip factors.</returns>
	public static ManipFactorResult Calculate(List<TimingDeviation> deviations, int keyCount)
	{
		var result = new ManipFactorResult();

		if (deviations == null || deviations.Count < 2 || keyCount < 2)
		{
			result.Success = false;
			return result;
		}

		// Filter out misses (notes that were never hit have no meaningful timing data)
		var validDeviations = deviations
			.Where(d => !d.WasNeverHit && d.Judgement != ManiaJudgement.Miss)
			.OrderBy(d => d.ExpectedTime)
			.ToList();

		if (validDeviations.Count < 2)
		{
			result.Success = false;
			return result;
		}

		// Split columns into left and right hand
		// Left hand: columns 0 to floor(keyCount/2) - 1
		// Right hand: columns ceil(keyCount/2) to keyCount - 1
		var leftHandEnd = keyCount / 2; // floor division in C#
		var rightHandStart = (keyCount + 1) / 2; // ceil division

		var leftHandColumns = Enumerable.Range(0, leftHandEnd).ToHashSet();
		var rightHandColumns = Enumerable.Range(rightHandStart, keyCount - rightHandStart).ToHashSet();

		// Generate per-column timing data
		var columnData = GenerateColumnData(validDeviations, keyCount);

		// Calculate deviations between adjacent columns for each hand
		var leftHandDeviations = CalculateHandDeviations(columnData, leftHandColumns);
		var rightHandDeviations = CalculateHandDeviations(columnData, rightHandColumns);

		// Combine all deviations
		var allDeviations = leftHandDeviations.Concat(rightHandDeviations).ToList();

		if (allDeviations.Count < 2)
		{
			result.Success = false;
			return result;
		}

		// Filter by percentiles (5th to 95th) and calculate mean
		result.ManipFactor = CalculateFilteredMean(allDeviations);
		result.LeftHandManip = leftHandDeviations.Count >= 2 ? CalculateFilteredMean(leftHandDeviations) : 0;
		result.RightHandManip = rightHandDeviations.Count >= 2 ? CalculateFilteredMean(rightHandDeviations) : 0;
		result.SampleCount = allDeviations.Count;
		result.Success = true;

		return result;
	}

	/// <summary>
	/// Generates timing data grouped by column.
	/// Each entry contains (expectedTime, actualTime) for notes in that column.
	/// </summary>
	private static Dictionary<int, List<(double ExpectedTime, double ActualTime)>> GenerateColumnData(
		List<TimingDeviation> deviations, int keyCount)
	{
		var columnData = new Dictionary<int, List<(double, double)>>();

		for (var col = 0; col < keyCount; col++)
			columnData[col] = deviations
				.Where(d => d.Column == col)
				.OrderBy(d => d.ExpectedTime)
				.Select(d => (d.ExpectedTime, d.ActualTime))
				.ToList();

		return columnData;
	}

	/// <summary>
	/// Calculates timing deviations between adjacent columns within a hand.
	/// For each note on column A, finds the nearest previous note on adjacent column B
	/// and calculates the timing deviation.
	/// </summary>
	private static List<double> CalculateHandDeviations(
		Dictionary<int, List<(double ExpectedTime, double ActualTime)>> columnData,
		HashSet<int> handColumns)
	{
		var deviations = new List<double>();
		var sortedColumns = handColumns.OrderBy(c => c).ToList();

		if (sortedColumns.Count < 2)
			return deviations;

		// For each pair of adjacent columns in the hand
		for (var i = 0; i < sortedColumns.Count - 1; i++)
		{
			var colA = sortedColumns[i];
			var colB = sortedColumns[i + 1];

			if (!columnData.ContainsKey(colA) || !columnData.ContainsKey(colB))
				continue;

			var dataA = columnData[colA];
			var dataB = columnData[colB];

			if (dataA.Count < 2 || dataB.Count < 2)
				continue;

			// Calculate deviations from A to B and B to A
			deviations.AddRange(CalculateColumnPairDeviations(dataA, dataB));
			deviations.AddRange(CalculateColumnPairDeviations(dataB, dataA));
		}

		return deviations;
	}

	/// <summary>
	/// For each note in column A, finds the nearest previous note in column B
	/// and calculates the timing deviation between expected and actual inter-column timing.
	/// </summary>
	private static List<double> CalculateColumnPairDeviations(
		List<(double ExpectedTime, double ActualTime)> colAData,
		List<(double ExpectedTime, double ActualTime)> colBData)
	{
		var deviations = new List<double>();
		var bIndex = 0;

		foreach (var noteA in colAData)
		{
			// Find the latest note in column B that comes before this note in column A
			while (bIndex < colBData.Count - 1 && colBData[bIndex + 1].ExpectedTime < noteA.ExpectedTime) bIndex++;

			if (bIndex >= colBData.Count)
				continue;

			var noteB = colBData[bIndex];

			// Only consider notes that are close enough in time (within 500ms)
			var expectedTimeDiff = noteA.ExpectedTime - noteB.ExpectedTime;
			if (expectedTimeDiff <= 0 || expectedTimeDiff > 500)
				continue;

			// Calculate the deviation:
			// Expected timing difference vs actual timing difference
			var actualTimeDiff = noteA.ActualTime - noteB.ActualTime;
			var deviation = actualTimeDiff - expectedTimeDiff;

			// Store absolute deviation (we care about magnitude, not direction)
			deviations.Add(Math.Abs(deviation));
		}

		return deviations;
	}

	/// <summary>
	/// Filters deviations by percentile (5th to 95th) and calculates the mean.
	/// Non-zero values only.
	/// </summary>
	private static double CalculateFilteredMean(List<double> deviations)
	{
		if (deviations.Count < 2)
			return 0;

		var sorted = deviations.OrderBy(x => x).ToList();

		var p5 = Percentile(sorted, 5);
		var p95 = Percentile(sorted, 95);

		// Filter values between 5th and 95th percentile, excluding zeros
		var filtered = deviations
			.Where(x => x > p5 && x < p95 && x > 0.001)
			.ToList();

		if (filtered.Count == 0)
			return 0;

		// Calculate mean and normalize to 0-1 scale
		// Typical manip deviation is 0-100ms, so divide by 100 to get 0-1 range
		var mean = filtered.Average();
		return mean / 100.0;
	}

	/// <summary>
	/// Calculates the value at a given percentile in a sorted list.
	/// </summary>
	private static double Percentile(List<double> sortedData, double percentile)
	{
		if (sortedData.Count == 0)
			return 0;

		if (sortedData.Count == 1)
			return sortedData[0];

		var index = percentile / 100.0 * (sortedData.Count - 1);
		var lowerIndex = (int)Math.Floor(index);
		var upperIndex = (int)Math.Ceiling(index);

		if (lowerIndex == upperIndex)
			return sortedData[lowerIndex];

		var fraction = index - lowerIndex;
		return sortedData[lowerIndex] + fraction * (sortedData[upperIndex] - sortedData[lowerIndex]);
	}
}
