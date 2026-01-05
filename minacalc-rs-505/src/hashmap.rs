use crate::{Ssr, wrapper::AllRates};
use crate::error::{MinaCalcError, MinaCalcResult};
use std::collections::HashMap;
pub trait HashMapCalcExt {
    /// Returns a HashMap where keys are music rates as strings (0.7, 0.8, ..., 2.0)
    /// and values are the corresponding skillset scores
    fn as_hashmap(&self) -> MinaCalcResult<HashMap<String, Ssr>>;
    
    /// Returns a HashMap with custom key formatting
    fn as_hashmap_with_format<F>(&self, formatter: F) -> MinaCalcResult<HashMap<String, Ssr>> where F: Fn(f32) -> String;

    /// Returns a HashMap with specific rate keys
    fn as_hashmap_with_rates(&self, rates: &[f32]) -> MinaCalcResult<HashMap<String, Ssr>> ;

    /// Gets scores for a specific rate
    fn get_rate_scores(&self, rate: f32) -> MinaCalcResult<Ssr>;

    /// Gets all available rates
    fn get_available_rates(&self) -> Vec<f32>;
}
impl HashMapCalcExt for AllRates {
    /// Returns a HashMap where keys are music rates as strings (0.7, 0.8, ..., 2.0)
    /// and values are the corresponding skillset scores
    fn as_hashmap(&self) -> MinaCalcResult<HashMap<String, Ssr>> {
        let mut map = HashMap::new();
        for (i, scores) in self.msds.iter().enumerate() {
            let rate = (i as f32) / 10.0 + 0.7;
            let key = format!("{:.1}", rate);
            map.insert(key, (*scores).into());
        }
        Ok(map)
    }
    
    /// Returns a HashMap with custom key formatting
    fn as_hashmap_with_format<F>(&self, formatter: F) -> MinaCalcResult<HashMap<String, Ssr>>
    where
        F: Fn(f32) -> String,
    {
        let mut map = HashMap::new();
        for (i, scores) in self.msds.iter().enumerate() {
            let rate = (i as f32) / 10.0 + 0.7;
            let key = formatter(rate);
            map.insert(key, (*scores).into());
        }
        Ok(map)
    }
    
    /// Returns a HashMap with specific rate keys
    fn as_hashmap_with_rates(&self, rates: &[f32]) -> MinaCalcResult<HashMap<String, Ssr>> {
        if rates.is_empty() {
            return Err(MinaCalcError::InvalidNoteData("No rates provided".to_string()));
        }
        
        let mut map = HashMap::new();
        for &rate in rates {
            if rate < 0.7 || rate > 2.0 {
                return Err(MinaCalcError::InvalidNoteData(format!("Rate {} is out of valid range [0.7, 2.0]", rate)));
            }
            
            let index = ((rate - 0.7) * 10.0).round() as usize;
            if index < self.msds.len() {
                let key = format!("{:.1}", rate);
                map.insert(key, self.msds[index].into());
            } else {
                return Err(MinaCalcError::InvalidNoteData(format!("Rate {} index {} out of bounds", rate, index)));
            }
        }
        Ok(map)
    }
    
    /// Gets scores for a specific rate
    fn get_rate_scores(&self, rate: f32) -> MinaCalcResult<Ssr> {
        if rate < 0.7 || rate > 2.0 {
            return Err(MinaCalcError::InvalidNoteData(format!("Rate {} is out of valid range [0.7, 2.0]", rate)));
        }
        
        let index = ((rate - 0.7) * 10.0).round() as usize;
        if index < self.msds.len() {
            Ok(self.msds[index].into())
        } else {
            Err(MinaCalcError::InvalidNoteData(format!("Rate {} index {} out of bounds", rate, index)))
        }
    }
    
    /// Gets all available rates
    fn get_available_rates(&self) -> Vec<f32> {
        (0..14).map(|i| (i as f32) / 10.0 + 0.7).collect()
    }
}
