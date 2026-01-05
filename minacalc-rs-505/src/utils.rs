use crate::{Ssr, wrapper::SkillsetScores};

/// Calculates the highest rated patterns from skillset scores
/// 
/// # Arguments
/// * `skillset` - The skillset scores to analyze
/// * `number` - The number of top patterns to return
/// 
/// # Returns
/// A vector of pattern names sorted by rating (highest first)
/// 
/// # Example
/// ```
/// use minacalc_rs::{SkillsetScores, utils::calculate_highest_patterns};
/// 
/// let skillset = SkillsetScores {
///     overall: 10.0,
///     stream: 8.0,
///     jumpstream: 12.0,
///     handstream: 6.0,
///     stamina: 4.0,
///     jackspeed: 2.0,
///     chordjack: 1.0,
///     technical: 3.0,
/// };
/// 
/// let top_patterns = calculate_highest_patterns(&skillset, 3);
/// // Returns: ["jumpstream", "stream", "handstream"]
/// ```
pub fn calculate_highest_patterns(skillset: &SkillsetScores, number: i8) -> Vec<String> {
    let patterns = vec![
        ("stream", skillset.stream),
        ("jumpstream", skillset.jumpstream),
        ("handstream", skillset.handstream),
        ("stamina", skillset.stamina),
        ("jackspeed", skillset.jackspeed),
        ("chordjack", skillset.chordjack),
        ("technical", skillset.technical),
    ];

    // Trier par rating décroissant
    let mut sorted_patterns: Vec<_> = patterns.into_iter().collect();
    sorted_patterns.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));

    // Prendre les N premiers patterns
    let top_patterns: Vec<String> = sorted_patterns
        .into_iter()
        .take(number as usize)
        .map(|(pattern, _)| pattern.to_string())
        .collect();

    top_patterns
}

/// Calculates the highest rated patterns from Ssr scores (converts to SkillsetScores first)
/// 
/// # Arguments
/// * `ssr` - The Ssr scores to analyze
/// * `number` - The number of top patterns to return
/// 
/// # Returns
/// A vector of pattern names sorted by rating (highest first)
pub fn calculate_highest_patterns_from_ssr(ssr: &Ssr, number: i8) -> Vec<String> {
    let skillset: SkillsetScores = (*ssr).into();
    calculate_highest_patterns(&skillset, number)
}
