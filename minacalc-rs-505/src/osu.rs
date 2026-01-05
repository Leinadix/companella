use rosu_map::{Beatmap};
use rosu_map::section::general::GameMode;
use rosu_map::section::hit_objects::HitObject;
use rosu_map::section::hit_objects::HitObjectKind;
use std::path::PathBuf;
use std::collections::HashMap;

use crate::{Calc, Note, wrapper::AllRates};
use crate::error::{OsuError, OsuResult, MinaCalcResult};

/// Extension trait for Calc to handle osu! beatmap operations
pub trait OsuCalcExt {
    /// Converts X position of a note to bitflag for 4K
    fn get_columns(x: f32) -> OsuResult<u32>;
    
    /// Converts a HitObject to Note for MinaCalc
    fn hit_object_to_note(hit_object: HitObject) -> OsuResult<Note>;
    
    /// Converts beatmap to notes with automatic merging at same time
    fn to_notes_merged(beatmap: &Beatmap) -> OsuResult<Vec<Note>>;
    
    /// Security check for beatmap validation
    fn security_check(beatmap: &Beatmap) -> OsuResult<()>;
    
    /// Calculates MSD from osu! file path
    fn calculate_msd_from_osu_file(&self, path: PathBuf) -> MinaCalcResult<AllRates>;
    
    /// Calculates MSD from osu! string
    fn calculate_msd_from_string(&self, string: String) -> MinaCalcResult<AllRates>;

    /// Calculates MSD for an arbitrary rate by scaling note times
    /// This allows any rate > 0, not just the predefined 0.7-2.0 rates
    fn calculate_msd_at_rate(&self, path: PathBuf, rate: f32) -> MinaCalcResult<crate::SkillsetScores>;
    
    /// Validates a collection of notes
    fn validate_notes(notes: &[Note]) -> OsuResult<()>;
}

impl OsuCalcExt for Calc {
    /// Converts X position of a note to bitflag for 4K
    fn get_columns(x: f32) -> OsuResult<u32> {
        match x {
            64.0 => Ok(1),  // bit flag 0b0001
            192.0 => Ok(2), // bit flag 0b0010
            320.0 => Ok(4), // bit flag 0b0100
            448.0 => Ok(8), // bit flag 0b1000
            _ => Err(OsuError::UnsupportedColumn(x))
        }
    }

    /// Converts a HitObject to Note for MinaCalc
    fn hit_object_to_note(hit_object: HitObject) -> OsuResult<Note> {
        let time = (hit_object.start_time as f32) / 1000.0; // Convert ms to seconds
        
        if time < 0.0 {
            return Err(OsuError::HitObjectConversion("Negative time not allowed".to_string()));
        }
        
        match hit_object.kind {
            HitObjectKind::Circle(hit_object) => {
                let notes = Self::get_columns(hit_object.pos.x)?;
                Ok(Note{notes, row_time: time})
            },
            HitObjectKind::Hold(hit_object) => {
                let notes = Self::get_columns(hit_object.pos_x)?;
                Ok(Note{notes, row_time: time})
            },
            _ => Err(OsuError::UnsupportedHitObjectKind(format!("{:#?}", hit_object.kind)))
        }
    }

    /// Converts beatmap to notes with automatic merging at same time
    fn to_notes_merged(beatmap: &Beatmap) -> OsuResult<Vec<Note>> {
        let mut time_notes: HashMap<i32, u32> = HashMap::new();
        
        // Convert and merge in one pass
        for hit_object in &beatmap.hit_objects {
            if let Ok(note) = Self::hit_object_to_note(hit_object.clone()) {
                // Convert time to integer for HashMap key (multiply by 1000 to preserve precision)
                let time_key = (note.row_time * 1000.0) as i32;
                
                // Merge bitflags for same time using OR operation
                time_notes.entry(time_key)
                    .and_modify(|existing_notes| *existing_notes |= note.notes)
                    .or_insert(note.notes);
            } else {
                return Err(OsuError::HitObjectConversion("Failed to convert hit object".to_string()));
            }
        }
        
        if time_notes.is_empty() {
            return Err(OsuError::HitObjectConversion("No valid notes found in beatmap".to_string()));
        }
        
        // Convert HashMap back to sorted Vec<Note>
        let mut notes: Vec<Note> = time_notes
            .into_iter()
            .map(|(time_key, notes)| Note { 
                notes, 
                row_time: (time_key as f32) / 1000.0 
            })
            .collect();
        
        // Sort by time
        notes.sort_by(|a, b| a.row_time.partial_cmp(&b.row_time).unwrap());
        
        // Validate all notes
        Self::validate_notes(&notes)?;
        
        Ok(notes)
    }

    fn security_check(beatmap: &Beatmap) -> OsuResult<()> {
        if beatmap.mode != GameMode::Mania {
            return Err(OsuError::UnsupportedGameMode(format!("{:?}", beatmap.mode)));
        }
        if beatmap.circle_size != 4.0 {
            return Err(OsuError::UnsupportedKeyCount(beatmap.circle_size));
        }
        Ok(())
    }
    
    fn validate_notes(notes: &[Note]) -> OsuResult<()> {
        if notes.is_empty() {
            return Err(OsuError::HitObjectConversion("No notes to validate".to_string()));
        }
        
        for (i, note) in notes.iter().enumerate() {
            if note.notes == 0 {
                return Err(OsuError::HitObjectConversion(format!("Note {} has no columns", i)));
            }
            if note.notes > 0b1111 {
                return Err(OsuError::HitObjectConversion(format!("Note {} exceeds 4K limit", i)));
            }
            if note.row_time < 0.0 {
                return Err(OsuError::HitObjectConversion(format!("Note {} has negative time", i)));
            }
        }
        
        // Check for duplicate times
        for i in 1..notes.len() {
            if notes[i].row_time == notes[i-1].row_time {
                return Err(OsuError::HitObjectConversion(format!("Duplicate time at index {}", i)));
            }
        }
        
        Ok(())
    }

    fn calculate_msd_from_osu_file(&self, path: PathBuf) -> MinaCalcResult<AllRates> {
        let beatmap: Beatmap = rosu_map::from_path(&path)
            .map_err(|e| OsuError::ParseFailed(format!("Failed to parse {}: {}", path.display(), e)))?;

        Self::security_check(&beatmap)?;
        let notes = Self::to_notes_merged(&beatmap)?;

        let msd = self.calc_msd(&notes)?;

        Ok(msd)
    }


    fn calculate_msd_from_string(&self, string: String) -> MinaCalcResult<AllRates> {
        let beatmap: Beatmap = rosu_map::from_str(&string)
            .map_err(|e| OsuError::ParseFailed(format!("Failed to parse string: {}", e)))?;

        Self::security_check(&beatmap)?;
        let notes = Self::to_notes_merged(&beatmap)?;

        let msd = self.calc_msd(&notes)?;
        Ok(msd)
    }
    
    fn calculate_msd_at_rate(&self, path: PathBuf, rate: f32) -> MinaCalcResult<crate::SkillsetScores> {
        use crate::error::MinaCalcError;
        
        if rate <= 0.0 {
            return Err(MinaCalcError::InvalidMusicRate(rate));
        }

        let beatmap: Beatmap = rosu_map::from_path(&path)
            .map_err(|e| OsuError::ParseFailed(format!("Failed to parse {}: {}", path.display(), e)))?;

        Self::security_check(&beatmap)?;
        let notes = Self::to_notes_merged(&beatmap)?;

        // Scale note times by 1/rate to simulate playing at different speed
        // Higher rate = faster = shorter times between notes = harder
        // So we divide times by rate (equivalent to multiplying by 1/rate)
        let scaled_notes: Vec<Note> = notes
            .iter()
            .map(|note| Note {
                notes: note.notes,
                row_time: note.row_time / rate,
            })
            .collect();

        // Calculate MSD on scaled notes
        let all_rates = self.calc_msd(&scaled_notes)?;

        // Return the 1.0x rate result (index 3 in the array)
        // Since we already scaled the notes, this gives us the MSD at the requested rate
        Ok(all_rates.msds[3])
    }
}