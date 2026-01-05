use minacalc_rs::{Calc, Note};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    println!("MinaCalc Rust Bindings - Exemple d'utilisation");
    println!("Version du calculateur: {}", Calc::version());
    
    // Créer une instance du calculateur
    let calc = Calc::new()?;
    
    // Créer des données de notes d'exemple
    // Ces notes représentent un pattern simple de 4 notes
    // notes: 0b1111 = 15 (4 notes, une sur chaque colonne)
    let notes = vec![
        Note { notes: 15, row_time: 0.0 },   // 4 notes au début (0b1111)
        Note { notes: 0, row_time: 0.5 },    // Pas de notes
        Note { notes: 15, row_time: 1.0 },   // 4 notes à 1 seconde (0b1111)
        Note { notes: 0, row_time: 1.5 },    // Pas de notes
        Note { notes: 15, row_time: 2.0 },   // 4 notes à 2 secondes (0b1111)
        Note { notes: 0, row_time: 2.5 },    // Pas de notes
        Note { notes: 15, row_time: 3.0 },   // 4 notes à 3 secondes (0b1111)
    ];
    
    println!("Calcul des scores MSD pour tous les taux de musique...");
    
    // Calculer les scores MSD pour tous les taux (0.7x à 2.0x)
    match calc.calc_msd(&notes) {
        Ok(msd_results) => {
            println!("Scores MSD calculés avec succès!");
            
            // Afficher les scores pour quelques taux
            let rates = [0.7, 1.0, 1.5, 2.0];
            let rate_indices = [0, 3, 8, 13]; // Indices correspondants dans le tableau
            
            for (rate, &index) in rates.iter().zip(rate_indices.iter()) {
                let scores = msd_results.msds[index];
                println!("Taux {:.1}x:", rate);
                println!("  Overall: {:.2}", scores.overall);
                println!("  Stream: {:.2}", scores.stream);
                println!("  Jumpstream: {:.2}", scores.jumpstream);
                println!("  Handstream: {:.2}", scores.handstream);
                println!("  Stamina: {:.2}", scores.stamina);
                println!("  Jack Speed: {:.2}", scores.jackspeed);
                println!("  Chordjack: {:.2}", scores.chordjack);
                println!("  Technical: {:.2}", scores.technical);
                println!();
            }
        }
        Err(e) => {
            eprintln!("Erreur lors du calcul MSD: {}", e);
        }
    }
    
    println!("Calcul des scores SSR pour un taux spécifique...");
    
    // Calculer les scores SSR pour un taux de 1.0x avec un objectif de 95%
    match calc.calc_ssr(&notes, 1.0, 90.0) {
        Ok(ssr_scores) => {
            println!("Scores SSR calculés avec succès pour 1.0x à 95%!");
            println!("Overall: {:.2}", ssr_scores.overall);
            println!("Stream: {:.2}", ssr_scores.stream);
            println!("Jumpstream: {:.2}", ssr_scores.jumpstream);
            println!("Handstream: {:.2}", ssr_scores.handstream);
            println!("Stamina: {:.2}", ssr_scores.stamina);
            println!("Jack Speed: {:.2}", ssr_scores.jackspeed);
            println!("Chordjack: {:.2}", ssr_scores.chordjack);
            println!("Technical: {:.2}", ssr_scores.technical);
        }
        Err(e) => {
            eprintln!("Erreur lors du calcul SSR: {}", e);
        }
    }
    
    Ok(())
}
