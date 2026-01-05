# MSD Calculator

A command line tool that analyzes osu!mania beatmaps and outputs MinaCalc Skill Difficulty (MSD) ratings in JSON format.

## Overview

This tool uses the [minacalc-rs](https://github.com/Glubus/minacalc-rs) library (Rust bindings for the Etterna MinaCalc difficulty calculator) to calculate MSD scores for osu!mania 4K beatmaps.

## Building

Requires Rust 1.70+ and a C++17 compatible compiler (MSVC on Windows).

```bash
cd msd-calculator
cargo build --release
```

The executable will be at `target/release/msd-calculator.exe`.

## Usage

### Basic Usage

Analyze a beatmap and output JSON to stdout:

```bash
msd-calculator "path/to/beatmap.osu"
```

### Options

```
msd-calculator [OPTIONS] <BEATMAP_FILE>

Arguments:
  <BEATMAP_FILE>  Path to the osu!mania beatmap file (.osu)

Options:
  -o, --output <OUTPUT>  Output file path (optional, prints to stdout if not specified)
  -r, --rate <RATE>      Only output scores for a specific rate (any value > 0, e.g., 1.0, 1.25, 0.85)
  -p, --pretty           Pretty print JSON output
  -h, --help             Print help
  -V, --version          Print version
```

### Examples

Pretty-printed full analysis:
```bash
msd-calculator -p "beatmap.osu"
```

Get only 1.0x rate scores:
```bash
msd-calculator --rate 1.0 "beatmap.osu"
```

Get scores for an arbitrary rate (e.g., 1.25x):
```bash
msd-calculator --rate 1.25 "beatmap.osu"
```

Save to file:
```bash
msd-calculator -o result.json "beatmap.osu"
```

## Output Format

### Full Analysis (all rates)

```json
{
  "beatmap_path": "path/to/beatmap.osu",
  "minacalc_version": 505,
  "rates": [
    {
      "rate": 0.7,
      "scores": {
        "overall": 8.52,
        "stream": 7.21,
        "jumpstream": 6.33,
        "handstream": 5.12,
        "stamina": 4.89,
        "jackspeed": 3.45,
        "chordjack": 2.78,
        "technical": 6.91
      }
    },
    ...
  ],
  "dominant_skillset": "stream",
  "difficulty_1x": 12.45
}
```

### Single Rate Analysis (--rate option)

```json
{
  "beatmap_path": "path/to/beatmap.osu",
  "minacalc_version": 505,
  "rate": 1.0,
  "scores": {
    "overall": 12.45,
    "stream": 11.21,
    "jumpstream": 9.33,
    "handstream": 8.12,
    "stamina": 7.89,
    "jackspeed": 5.45,
    "chordjack": 4.78,
    "technical": 10.91
  },
  "dominant_skillset": "stream"
}
```

## Skillsets

- **overall**: General difficulty rating
- **stream**: Consecutive single-note patterns
- **jumpstream**: Jump patterns within streams
- **handstream**: Two-handed alternating patterns
- **stamina**: Endurance requirements
- **jackspeed**: Jack (repeated column) speed
- **chordjack**: Chord jack patterns
- **technical**: Technical complexity

## Rate Support

- **Without `--rate` flag**: Returns MSD for predefined rates (0.7x to 2.0x in 0.1 increments)
- **With `--rate` flag**: Supports ANY rate > 0 (e.g., 0.85, 1.25, 1.05, 3.0)
  - Predefined rates (0.7, 0.8, ..., 2.0) use cached calculations for speed
  - Arbitrary rates calculate MSD by scaling note timing

## Limitations

- Only supports osu!mania 4K beatmaps
- Requires valid .osu file format
- Hold notes are treated as single notes at their start time

## Integration

This tool is designed to be used by OsuMappingHelper. See `MsdAnalyzer.cs` for the C# wrapper service.
