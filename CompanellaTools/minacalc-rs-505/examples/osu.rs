use minacalc_rs::{Calc, OsuCalcExt, HashMapCalcExt, AllRates};
use std::path::PathBuf;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    println!("=== MinaCalc Osu! Beatmap Processing Example ===");
    
    let calc = Calc::new()?;
    println!("✅ Calculator created successfully");
    
    // Example 1: Process a beatmap file
    let beatmap_path = PathBuf::from("assets/test.osu");
    
    if beatmap_path.exists() {
        println!("📁 Processing beatmap: {}", beatmap_path.display());
        
        let msd_results: AllRates = calc.calculate_msd_from_osu_file(beatmap_path)?;
        println!("✅ Beatmap processed successfully!");
        
        // Method 1: Direct access to specific rates
        println!("\n--- Direct Access Method ---");
        let rates = [0.7, 1.0, 1.5, 2.0];
        let rate_indices = [0, 3, 8, 13];
        
        for (rate, &index) in rates.iter().zip(rate_indices.iter()) {
            if index < msd_results.msds.len() {
                let scores = msd_results.msds[index];
                println!("{:.1}x: Overall={:.2}, Stream={:.2}, Tech={:.2}", 
                         rate, scores.overall, scores.stream, scores.technical);
            }
        }
        
        // Method 2: Using HashMap conversion (convert to wrapper type first)
        println!("\n--- HashMap Method ---");
        let hashmap_results = msd_results;
        let hashmap = hashmap_results.as_hashmap()?;
        
        for (rate, scores) in hashmap.iter() {
            println!("{}: Overall={:.2}, Stream={:.2}, Tech={:.2}", 
                     rate, scores.overall, scores.stream, scores.technical);
        }
        
        // Method 3: Get specific rates with HashMap
        println!("\n--- Specific Rate Access ---");
        if let Some(scores) = hashmap.get("1.0") {
            println!("1.0x rate: Overall={:.2}, Stream={:.2}", 
                     scores.overall, scores.stream);
        }
        
        if let Some(scores) = hashmap.get("2.0") {
            println!("2.0x rate: Overall={:.2}, Stream={:.2}", 
                     scores.overall, scores.stream);
        }
        
    } else {
        println!("⚠️  Beatmap file not found: {}", beatmap_path.display());
        println!("   Creating sample notes instead...");
        
        // Example 2: Manual notes with HashMap
        let notes = vec![
            minacalc_rs::Note { notes: 1, row_time: 0.0 },    // Left column
            minacalc_rs::Note { notes: 8, row_time: 1.0 },    // Right column
            minacalc_rs::Note { notes: 15, row_time: 2.0 },   // All columns
        ];
        
        let msd_results = calc.calc_msd(&notes)?;
        let hashmap = msd_results.as_hashmap()?;
        
        println!("✅ Sample notes processed!");
        println!("Available rates: {:?}", hashmap.keys().collect::<Vec<_>>());
        
        for (rate, scores) in hashmap.iter().take(3) {
            println!("{}: Overall={:.2}, Stream={:.2}", 
                     rate, scores.overall, scores.stream);
        }
    }
    
    println!("\n🎉 Example completed successfully!");
    Ok(())
}