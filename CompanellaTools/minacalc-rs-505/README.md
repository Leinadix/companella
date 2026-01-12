# MinaCalc Rust Bindings

[![Crates.io](https://img.shields.io/crates/v/minacalc-rs)](https://crates.io/crates/minacalc-rs)
[![Documentation](https://docs.rs/minacalc-rs/badge.svg)](https://docs.rs/minacalc-rs)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Safe and ergonomic Rust bindings for the MinaCalc rhythm game difficulty calculator. This crate provides high-level Rust APIs for calculating difficulty scores in rhythm games like StepMania, Etterna, and osu!.

## Features

- **Core Difficulty Calculation**: Calculate MSD (MinaCalc Skill Difficulty) scores for rhythm game charts
- **Multi-rate Support**: Get difficulty scores for music rates from 0.7x to 2.0x
- **Pattern Analysis**: Analyze specific skillsets like stream, jumpstream, handstream, stamina, and more
- **Thread-safe**: Multi-threaded calculator pool for high-performance applications
- **osu! Integration**: Parse and analyze osu! beatmap files directly
- **Utility Functions**: Helper functions for pattern analysis and difficulty comparison
- **HashMap Conversion**: Easy conversion to HashMap format for flexible data handling

## Prerequisites

- **Rust**: Edition 2021 or later
- **C++ Compiler**: C++17 compatible compiler (MSVC on Windows, GCC/Clang on Unix)
- **Build Tools**: `cc` and `bindgen` (automatically handled by Cargo)

## Installation

Add this to your `Cargo.toml`:

```toml
[dependencies]
minacalc-rs = "0.2.1"
```

### Feature Flags

The crate supports several optional features:

```toml
[dependencies]
minacalc-rs = { version = "0.2.1", features = ["hashmap", "thread", "osu", "utils"] }
```

- **`hashmap`** (default): Provides HashMap conversion for MSD results
- **`thread`**: Provides thread-safe calculator pool
- **`osu`**: Provides osu! beatmap parsing and calculation
- **`utils`**: Provides utility functions for pattern analysis

## Quick Start

### Basic Usage

```rust
use minacalc_rs::{Calc, Note};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    // Create a calculator instance
    let calc = Calc::new()?;
    
    // Define some notes (4K chart)
    let notes = vec![
        Note { notes: 1, row_time: 0.0 },    // Left column at 0s
        Note { notes: 8, row_time: 1.0 },    // Right column at 1s
        Note { notes: 15, row_time: 2.0 },   // All columns at 2s
    ];
    
    // Calculate MSD scores for all rates
    let msd_results = calc.calc_msd(&notes)?;
    
    // Access scores for specific rates
    let overall_1x = msd_results.msds[3].overall;  // 1.0x rate
    let stream_2x = msd_results.msds[13].stream;   // 2.0x rate
    
    println!("1.0x Overall: {:.2}", overall_1x);
    println!("2.0x Stream: {:.2}", stream_2x);
    
    Ok(())
}
```

### HashMap Conversion

```rust
use minacalc_rs::{Calc, Note, HashMapCalcExt};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let calc = Calc::new()?;
    let notes = vec![
        Note { notes: 1, row_time: 0.0 },
        Note { notes: 8, row_time: 1.0 },
    ];
    
    let msd_results = calc.calc_msd(&notes)?;
    
    // Convert to HashMap for easy access
    let hashmap = msd_results.as_hashmap()?;
    
    // Access by rate string
    if let Some(scores) = hashmap.get("1.0") {
        println!("1.0x: Overall={:.2}, Stream={:.2}", 
                 scores.overall, scores.stream);
    }
    
    Ok(())
}
```

### osu! Beatmap Analysis

```rust
use minacalc_rs::{Calc, OsuCalcExt};
use std::path::PathBuf;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let calc = Calc::new()?;
    
    // Analyze an osu! beatmap file
    let beatmap_path = PathBuf::from("path/to/beatmap.osu");
    let msd_results = calc.calculate_msd_from_osu_file(beatmap_path)?;
    
    // Access scores for different rates
    let rates = [0.7, 1.0, 1.5, 2.0];
    let rate_indices = [0, 3, 8, 13];
    
    for (rate, &index) in rates.iter().zip(rate_indices.iter()) {
        if index < msd_results.msds.len() {
            let scores = msd_results.msds[index];
            println!("{:.1}x: Overall={:.2}, Stream={:.2}, Tech={:.2}", 
                     rate, scores.overall, scores.stream, scores.technical);
        }
    }
    
    Ok(())
}
```

### Pattern Analysis with Utils

```rust
use minacalc_rs::{Calc, Note, utils::*};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let calc = Calc::new()?;
    let notes = vec![
        Note { notes: 1, row_time: 0.0 },
        Note { notes: 8, row_time: 1.0 },
    ];
    
    let msd_results = calc.calc_msd(&notes)?;
    
    // Get top 3 patterns for 1.0x rate
    let top_patterns = calculate_highest_patterns(&msd_results.msds[3], 3);
    println!("Top 3 patterns: {:?}", top_patterns);
    
    // Get all patterns ranked by difficulty
    let all_patterns = calculate_highest_patterns(&msd_results.msds[3], 7);
    println!("All patterns ranked: {:?}", all_patterns);
    
    Ok(())
}
```

### Thread-safe Calculator Pool

```rust
use minacalc_rs::thread::{CalcPool, Note};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    // Create a pool of calculators
    let pool = CalcPool::new(4)?; // 4 calculator instances
    
    let notes = vec![
        Note { notes: 1, row_time: 0.0 },
        Note { notes: 8, row_time: 1.0 },
    ];
    
    // Calculate MSD using the pool
    let msd_results = pool.calc_msd(&notes)?;
    
    println!("Overall difficulty: {:.2}", msd_results.msds[3].overall);
    
    Ok(())
}
```

## API Reference

### Core Types

- **`Calc`**: Main calculator instance
- **`Note`**: Represents a note in a rhythm game chart
- **`AllRates`**: Contains MSD scores for all music rates (0.7x to 2.0x)
- **`SkillsetScores`**: Individual skillset scores (stream, jumpstream, etc.)

### Skillsets

The calculator provides scores for these skillsets:

- **Overall**: General difficulty rating
- **Stream**: Consecutive note patterns
- **Jumpstream**: Jump patterns in streams
- **Handstream**: Two-handed patterns
- **Stamina**: Endurance requirements
- **Jackspeed**: Jack pattern speed
- **Chordjack**: Chord jack patterns
- **Technical**: Technical complexity

### Music Rates

Scores are calculated for 14 different music rates:
- 0.7x, 0.8x, 0.9x, 1.0x, 1.1x, 1.2x, 1.3x, 1.4x, 1.5x, 1.6x, 1.7x, 1.8x, 1.9x, 2.0x

## Examples

The crate includes several examples demonstrating different use cases:

- **`basic_usage`**: Basic MSD calculation
- **`osu`**: osu! beatmap analysis
- **`utils_example`**: Pattern analysis utilities

Run examples with:

```bash
# Basic usage
cargo run --example basic_usage

# osu! beatmap analysis (requires osu feature)
cargo run --example osu --features="osu hashmap"

# Utils example (requires utils feature)
cargo run --example utils_example --features="utils"
```

## Error Handling

The crate uses custom error types for different failure modes:

- **`MinaCalcError`**: General calculation errors
- **`OsuError`**: osu! beatmap parsing errors

All functions return `Result<T, E>` for proper error handling.

## Thread Safety

The `Calc` type is not `Send` or `Sync` by default. For multi-threaded applications, use the `CalcPool` from the `thread` feature, which provides a thread-safe pool of calculator instances.

## Performance

- **Single-threaded**: Optimized for single-threaded applications
- **Multi-threaded**: Use `CalcPool` for concurrent calculations
- **Memory**: Efficient memory usage with minimal allocations
- **Caching**: Calculator instances can be reused for multiple calculations

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- **MinaCalc**: The original C++ difficulty calculator
- **Etterna**: The rhythm game that inspired this project
- **Rust Community**: For the excellent tooling and ecosystem

## Changelog

### v0.2.1
- Added `utils` feature with pattern analysis functions

### v0.2.0
- Added `osu` feature for beatmap parsing
- Added `thread` feature for thread-safe calculator pools
- Improved HashMap conversion utilities
- Enhanced documentation and examples
- Improved type naming (`AllRates` instead of `MsdForAllRates`)
- Enhanced error handling and documentation
- Fixed namespace conflicts in bindings

### v0.1.0
- Initial release with basic MSD calculation
- Core calculator functionality
- Basic Rust bindings for MinaCalc
