//! MSD Calculator CLI Tool (MinaCalc 5.05)
//!
//! A command line tool that analyzes osu!mania beatmaps and outputs
//! MinaCalc Skill Difficulty (MSD) ratings in JSON format.

use clap::Parser;
use minacalc_rs::{Calc, OsuCalcExt, AllRates, SkillsetScores};
use serde::{Serialize, Deserialize};
use std::path::PathBuf;
use std::process::ExitCode;

/// MSD Calculator - Analyze osu!mania beatmaps for difficulty ratings
#[derive(Parser, Debug)]
#[command(name = "msd-calculator-505")]
#[command(author = "OsuMappingHelper")]
#[command(version = "1.0.0")]
#[command(about = "Calculate MinaCalc MSD difficulty ratings for osu!mania beatmaps (MinaCalc 5.05)")]
struct Args {
    /// Path to the osu!mania beatmap file (.osu)
    #[arg(required = true)]
    beatmap_file: PathBuf,

    /// Output file path (optional, prints to stdout if not specified)
    #[arg(short, long)]
    output: Option<PathBuf>,

    /// Only output scores for a specific rate (any value > 0, e.g., 1.0, 1.25, 0.85)
    #[arg(short, long)]
    rate: Option<f32>,

    /// Pretty print JSON output
    #[arg(short, long)]
    pretty: bool,
}

/// Represents a single skillset score set for JSON output
#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct SkillsetOutput {
    pub overall: f32,
    pub stream: f32,
    pub jumpstream: f32,
    pub handstream: f32,
    pub stamina: f32,
    pub jackspeed: f32,
    pub chordjack: f32,
    pub technical: f32,
}

impl From<SkillsetScores> for SkillsetOutput {
    fn from(scores: SkillsetScores) -> Self {
        SkillsetOutput {
            overall: round_2(scores.overall),
            stream: round_2(scores.stream),
            jumpstream: round_2(scores.jumpstream),
            handstream: round_2(scores.handstream),
            stamina: round_2(scores.stamina),
            jackspeed: round_2(scores.jackspeed),
            chordjack: round_2(scores.chordjack),
            technical: round_2(scores.technical),
        }
    }
}

/// Represents a single rate entry in the output
#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct RateEntry {
    pub rate: f32,
    pub scores: SkillsetOutput,
}

/// Full MSD analysis result for JSON output (without --rate flag)
#[derive(Serialize, Deserialize, Debug)]
pub struct MsdResult {
    /// Path to the analyzed beatmap file
    pub beatmap_path: String,
    
    /// MinaCalc version used
    pub minacalc_version: i32,
    
    /// MSD scores for predefined rates (0.7x to 2.0x in 0.1 increments)
    /// For arbitrary rates, use the --rate flag instead
    pub rates: Vec<RateEntry>,
    
    /// Dominant skillset at 1.0x rate (the highest non-overall score)
    pub dominant_skillset: String,
    
    /// Overall difficulty at 1.0x rate
    pub difficulty_1x: f32,
}

/// Single rate MSD result for when --rate is specified
#[derive(Serialize, Deserialize, Debug)]
pub struct SingleRateMsdResult {
    /// Path to the analyzed beatmap file
    pub beatmap_path: String,
    
    /// MinaCalc version used
    pub minacalc_version: i32,
    
    /// The requested rate
    pub rate: f32,
    
    /// MSD scores for the requested rate
    pub scores: SkillsetOutput,
    
    /// Dominant skillset at this rate
    pub dominant_skillset: String,
}

/// Round a float to 2 decimal places
fn round_2(value: f32) -> f32 {
    (value * 100.0).round() / 100.0
}

/// Rate index to rate value mapping (0.7x to 2.0x)
const RATES: [f32; 14] = [0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.0];

/// Find the rate index for a given rate value (if it matches a predefined rate)
fn find_rate_index(rate: f32) -> Option<usize> {
    // Very tight tolerance (0.0005) to only match exact predefined rates
    // This allows 0.001x precision for arbitrary rates
    for (i, &r) in RATES.iter().enumerate() {
        if (r - rate).abs() < 0.0005 {
            return Some(i);
        }
    }
    None
}

/// Check if rate is valid (any rate > 0)
fn is_valid_rate(rate: f32) -> bool {
    rate > 0.0
}

/// Get the dominant skillset name from scores (highest non-overall score)
fn get_dominant_skillset(scores: &SkillsetScores) -> String {
    let skillsets = [
        ("stream", scores.stream),
        ("jumpstream", scores.jumpstream),
        ("handstream", scores.handstream),
        ("stamina", scores.stamina),
        ("jackspeed", scores.jackspeed),
        ("chordjack", scores.chordjack),
        ("technical", scores.technical),
    ];
    
    skillsets
        .iter()
        .max_by(|a, b| a.1.partial_cmp(&b.1).unwrap_or(std::cmp::Ordering::Equal))
        .map(|(name, _)| name.to_string())
        .unwrap_or_else(|| "unknown".to_string())
}

/// Convert AllRates to our output format
fn convert_to_output(all_rates: &AllRates, beatmap_path: &str) -> MsdResult {
    let mut rates = Vec::with_capacity(14);
    
    for (i, &rate) in RATES.iter().enumerate() {
        let scores = all_rates.msds[i];
        rates.push(RateEntry {
            rate,
            scores: scores.into(),
        });
    }
    
    // Get 1.0x rate (index 3)
    let scores_1x = all_rates.msds[3];
    let dominant = get_dominant_skillset(&scores_1x);
    
    MsdResult {
        beatmap_path: beatmap_path.to_string(),
        minacalc_version: Calc::version(),
        rates,
        dominant_skillset: dominant,
        difficulty_1x: round_2(scores_1x.overall),
    }
}

/// Convert a single rate to output format
fn convert_single_rate(
    all_rates: &AllRates,
    rate_index: usize,
    rate: f32,
    beatmap_path: &str,
) -> SingleRateMsdResult {
    let scores = all_rates.msds[rate_index];
    let dominant = get_dominant_skillset(&scores);
    
    SingleRateMsdResult {
        beatmap_path: beatmap_path.to_string(),
        minacalc_version: Calc::version(),
        rate,
        scores: scores.into(),
        dominant_skillset: dominant,
    }
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let args = Args::parse();
    
    // Validate input file exists
    if !args.beatmap_file.exists() {
        return Err(format!("Beatmap file not found: {}", args.beatmap_file.display()).into());
    }
    
    // Create calculator
    let calc = Calc::new()?;
    
    // Calculate MSD from beatmap file
    let all_rates = calc.calculate_msd_from_osu_file(args.beatmap_file.clone())?;
    
    let beatmap_path_str = args.beatmap_file.to_string_lossy().to_string();
    
    // Generate JSON output
    let json_output = if let Some(rate) = args.rate {
        // Validate rate is positive
        if !is_valid_rate(rate) {
            return Err(format!("Invalid rate: {}. Rate must be greater than 0.", rate).into());
        }
        
        // Check if rate matches a predefined rate
        if let Some(rate_index) = find_rate_index(rate) {
            // Use predefined rate from AllRates
            let result = convert_single_rate(&all_rates, rate_index, RATES[rate_index], &beatmap_path_str);
            
            if args.pretty {
                serde_json::to_string_pretty(&result)?
            } else {
                serde_json::to_string(&result)?
            }
        } else {
            // Arbitrary rate - calculate using scaled note times
            let scores = calc.calculate_msd_at_rate(args.beatmap_file.clone(), rate)?;
            let dominant = get_dominant_skillset(&scores);
            
            let result = SingleRateMsdResult {
                beatmap_path: beatmap_path_str.clone(),
                minacalc_version: Calc::version(),
                rate,
                scores: scores.into(),
                dominant_skillset: dominant,
            };
            
            if args.pretty {
                serde_json::to_string_pretty(&result)?
            } else {
                serde_json::to_string(&result)?
            }
        }
    } else {
        // Full output with all rates
        let result = convert_to_output(&all_rates, &beatmap_path_str);
        
        if args.pretty {
            serde_json::to_string_pretty(&result)?
        } else {
            serde_json::to_string(&result)?
        }
    };
    
    // Output result
    if let Some(output_path) = args.output {
        std::fs::write(&output_path, &json_output)?;
        eprintln!("Results written to: {}", output_path.display());
    } else {
        println!("{}", json_output);
    }
    
    Ok(())
}

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("Error: {}", e);
            ExitCode::FAILURE
        }
    }
}
