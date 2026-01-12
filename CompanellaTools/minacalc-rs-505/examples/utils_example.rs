use minacalc_rs::{Ssr, SkillsetScores, utils::*};

fn main() {
    println!("=== MinaCalc Utils Example ===");
    
    // Test avec le type Ssr (bindings C)
    let ssr = Ssr {
        overall: 10.0,
        stream: 8.0,
        jumpstream: 12.0,
        handstream: 6.0,
        stamina: 4.0,
        jackspeed: 2.0,
        chordjack: 1.0,
        technical: 3.0,
    };
    
    println!("Scores Ssr:");
    println!("  Overall: {:.2}", ssr.overall);
    println!("  Stream: {:.2}", ssr.stream);
    println!("  Jumpstream: {:.2}", ssr.jumpstream);
    println!("  Handstream: {:.2}", ssr.handstream);
    println!("  Stamina: {:.2}", ssr.stamina);
    println!("  Jackspeed: {:.2}", ssr.jackspeed);
    println!("  Chordjack: {:.2}", ssr.chordjack);
    println!("  Technical: {:.2}", ssr.technical);
    
    let top_3_patterns = calculate_highest_patterns_from_ssr(&ssr, 3);
    println!("\nTop 3 patterns: {:?}", top_3_patterns);
    
    let top_5_patterns = calculate_highest_patterns_from_ssr(&ssr, 5);
    println!("Top 5 patterns: {:?}", top_5_patterns);
    
    // Test avec le type SkillsetScores (wrapper)
    let skillset = SkillsetScores {
        overall: 15.0,
        stream: 7.0,
        jumpstream: 9.0,
        handstream: 11.0,
        stamina: 5.0,
        jackspeed: 3.0,
        chordjack: 2.0,
        technical: 8.0,
    };
    
    println!("\nScores SkillsetScores:");
    println!("  Overall: {:.2}", skillset.overall);
    println!("  Stream: {:.2}", skillset.stream);
    println!("  Jumpstream: {:.2}", skillset.jumpstream);
    println!("  Handstream: {:.2}", skillset.handstream);
    println!("  Stamina: {:.2}", skillset.stamina);
    println!("  Jackspeed: {:.2}", skillset.jackspeed);
    println!("  Chordjack: {:.2}", skillset.chordjack);
    println!("  Technical: {:.2}", skillset.technical);
    
    let top_2_patterns = calculate_highest_patterns(&skillset, 2);
    println!("\nTop 2 patterns: {:?}", top_2_patterns);
    
    let all_patterns = calculate_highest_patterns(&skillset, 7);
    println!("All patterns ranked: {:?}", all_patterns);
    
    println!("\n🎉 Utils example completed successfully!");
}
