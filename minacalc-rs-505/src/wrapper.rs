use crate::{NoteInfo, Ssr, MsdForAllRates as BindingsMsdForAllRates, CalcHandle, create_calc, calc_version, calc_msd, calc_ssr, destroy_calc};
use crate::error::{MinaCalcError, MinaCalcResult};

/// Represents a note in the rhythm game
#[derive(Debug, Clone, Copy)]
pub struct Note {
    /// Number of notes at this time position
    pub notes: u32,
    /// Row time (in seconds)
    pub row_time: f32,
}

impl Note {
    /// Validates note data
    pub fn validate(&self) -> MinaCalcResult<()> {
        if self.notes == 0 {
            return Err(MinaCalcError::InvalidNoteData("Note must have at least one column".to_string()));
        }
        if self.notes > 0b1111 {
            return Err(MinaCalcError::InvalidNoteData("Note bitflags exceed 4K limit".to_string()));
        }
        if self.row_time < 0.0 {
            return Err(MinaCalcError::InvalidNoteData("Row time cannot be negative".to_string()));
        }
        Ok(())
    }
}

impl From<Note> for NoteInfo {
    fn from(note: Note) -> Self {
        NoteInfo {
            notes: note.notes,
            rowTime: note.row_time,
        }
    }
}

impl From<NoteInfo> for Note {
    fn from(note_info: NoteInfo) -> Self {
        Note {
            notes: note_info.notes,
            row_time: note_info.rowTime,
        }
    }
}

/// Represents difficulty scores for different skillsets
#[derive(Debug, Clone, Copy)]
pub struct SkillsetScores {
    pub overall: f32,
    pub stream: f32,
    pub jumpstream: f32,
    pub handstream: f32,
    pub stamina: f32,
    pub jackspeed: f32,
    pub chordjack: f32,
    pub technical: f32,
}

impl SkillsetScores {
    /// Validates scores are within reasonable bounds
    pub fn validate(&self) -> MinaCalcResult<()> {
        let scores = [
            self.overall, self.stream, self.jumpstream, self.handstream,
            self.stamina, self.jackspeed, self.chordjack, self.technical
        ];
        
        for score in scores {
            if score < 0.0 || score > 1000.0 {
                return Err(MinaCalcError::InvalidNoteData(format!("Score {} is out of reasonable bounds", score)));
            }
        }
        Ok(())
    }
}

impl From<Ssr> for SkillsetScores {
    fn from(ssr: Ssr) -> Self {
        SkillsetScores {
            overall: ssr.overall,
            stream: ssr.stream,
            jumpstream: ssr.jumpstream,
            handstream: ssr.handstream,
            stamina: ssr.stamina,
            jackspeed: ssr.jackspeed,
            chordjack: ssr.chordjack,
            technical: ssr.technical,
        }
    }
}

impl From<SkillsetScores> for Ssr {
    fn from(scores: SkillsetScores) -> Self {
        Ssr {
            overall: scores.overall,
            stream: scores.stream,
            jumpstream: scores.jumpstream,
            handstream: scores.handstream,
            stamina: scores.stamina,
            jackspeed: scores.jackspeed,
            chordjack: scores.chordjack,
            technical: scores.technical,
        }
    }
}

/// Represents MSD scores for all music rates (0.7x to 2.0x)
#[derive(Debug, Clone)]
pub struct AllRates {
    pub msds: [SkillsetScores; 14],
}

impl AllRates {
    /// Validates all MSD scores
    pub fn validate(&self) -> MinaCalcResult<()> {
        for (i, scores) in self.msds.iter().enumerate() {
            scores.validate()
                .map_err(|e| MinaCalcError::InvalidNoteData(format!("Rate {}: {}", (i as f32) / 10.0 + 0.7, e)))?;
        }
        Ok(())
    }
}

impl From<AllRates> for super::MsdForAllRates {
    fn from(msd: AllRates) -> Self {
        let mut bindings_msd = super::MsdForAllRates {
            msds: [Ssr {
                overall: 0.0,
                stream: 0.0,
                jumpstream: 0.0,
                handstream: 0.0,
                stamina: 0.0,
                jackspeed: 0.0,
                chordjack: 0.0,
                technical: 0.0,
            }; 14],
        };
        
        for (i, scores) in msd.msds.iter().enumerate() {
            bindings_msd.msds[i] = (*scores).into();
        }
        
        bindings_msd
    }
}

impl From<BindingsMsdForAllRates> for AllRates {
    fn from(bindings_msd: BindingsMsdForAllRates) -> Self {
        let mut msds = [SkillsetScores {
            overall: 0.0,
            stream: 0.0,
            jumpstream: 0.0,
            handstream: 0.0,
            stamina: 0.0,
            jackspeed: 0.0,
            chordjack: 0.0,
            technical: 0.0,
        }; 14];
        
        for (i, ssr) in bindings_msd.msds.iter().enumerate() {
            msds[i] = (*ssr).into();
        }
        
        AllRates { msds }
    }
}

/// Main handler for difficulty calculations
#[derive(Clone)]
pub struct Calc {
    handle: *mut CalcHandle,
}

impl Calc {
    /// Creates a new calculator instance
    pub fn new() -> MinaCalcResult<Self> {
        let handle = unsafe { create_calc() };
        if handle.is_null() {
            return Err(MinaCalcError::CalculatorCreationFailed);
        }
        Ok(Calc { handle })
    }
    
    /// Gets the calculator version
    pub fn version() -> i32 {
        unsafe { calc_version() }
    }
    
    /// Calculates MSD scores for all music rates
    pub fn calc_msd(&self, notes: &[Note]) -> MinaCalcResult<AllRates> {
        if notes.is_empty() {
            return Err(MinaCalcError::NoNotesProvided);
        }
        
        // Validate all notes
        for note in notes {
            note.validate()?;
        }
        
        // Convert notes to C format
        let note_infos: Vec<NoteInfo> = notes.iter().map(|&note| note.into()).collect();
        
        let result = unsafe {
            calc_msd(self.handle, note_infos.as_ptr(), note_infos.len())
        };
        
        let msd: AllRates = result.into();
        msd.validate()?;
        Ok(msd)
    }
    
    /// Calculates SSR scores for a specific music rate and score goal
    pub fn calc_ssr(
        &self,
        notes: &[Note],
        music_rate: f32,
        score_goal: f32,
    ) -> MinaCalcResult<SkillsetScores> {
        if notes.is_empty() {
            return Err(MinaCalcError::NoNotesProvided);
        }
        
        if music_rate <= 0.0 {
            return Err(MinaCalcError::InvalidMusicRate(music_rate));
        }
        
        if score_goal <= 0.0 || score_goal > 100.0 {
            return Err(MinaCalcError::InvalidScoreGoal(score_goal));
        }
        
        // Validate all notes
        for note in notes {
            note.validate()?;
        }
        
        // Convert notes to C format
        let mut note_infos: Vec<NoteInfo> = notes.iter().map(|&note| note.into()).collect();
        
        let result = unsafe {
            calc_ssr(self.handle, note_infos.as_mut_ptr(), note_infos.len(), music_rate, score_goal)
        };
        
        let scores: SkillsetScores = result.into();
        scores.validate()?;
        Ok(scores)
    }
    
    /// Validates the calculator handle is still valid
    pub fn is_valid(&self) -> bool {
        !self.handle.is_null()
    }
}

impl Drop for Calc {
    fn drop(&mut self) {
        if !self.handle.is_null() {
            unsafe {
                destroy_calc(self.handle);
            }
        }
    }
}

impl Default for Calc {
    fn default() -> Self {
        Self::new().expect("Failed to create default calculator")
    }
}

// Unit tests
#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_calc_version() {
        let version = Calc::version();
        assert!(version > 0);
    }
    
    #[test]
    fn test_calc_creation() {
        let calc = Calc::new();
        assert!(calc.is_ok());
    }
    
    #[test]
    fn test_note_conversion() {
        let note = Note {
            notes: 4,
            row_time: 1.5,
        };
        
        let note_info: NoteInfo = note.into();
        let converted_note: Note = note_info.into();
        
        assert_eq!(note.notes, converted_note.notes);
        assert_eq!(note.row_time, converted_note.row_time);
    }
    
    #[test]
    fn test_skillset_scores_conversion() {
        let scores = SkillsetScores {
            overall: 10.5,
            stream: 8.2,
            jumpstream: 12.1,
            handstream: 9.3,
            stamina: 7.8,
            jackspeed: 11.4,
            chordjack: 6.9,
            technical: 13.2,
        };
        
        let ssr: Ssr = scores.into();
        let converted_scores: SkillsetScores = ssr.into();
        
        assert_eq!(scores.overall, converted_scores.overall);
        assert_eq!(scores.stream, converted_scores.stream);
        assert_eq!(scores.jumpstream, converted_scores.jumpstream);
        assert_eq!(scores.handstream, converted_scores.handstream);
        assert_eq!(scores.stamina, converted_scores.stamina);
        assert_eq!(scores.jackspeed, converted_scores.jackspeed);
        assert_eq!(scores.chordjack, converted_scores.chordjack);
        assert_eq!(scores.technical, converted_scores.technical);
    }
}
