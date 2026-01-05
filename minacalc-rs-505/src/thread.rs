use crate::Calc;
use crate::error::{MinaCalcError, MinaCalcResult};
use std::sync::Arc;
use parking_lot::Mutex;
use once_cell::sync::Lazy;

// SAFETY: We assume CalcHandle is thread-safe from C++
// If this is not the case, remove these implementations
unsafe impl Send for Calc {}
unsafe impl Sync for Calc {}

/// Thread-safe calculator pool
pub struct ThreadSafeCalcPool {
    calculators: Arc<Mutex<Vec<Calc>>>,
}

/// Global calculator pool for thread-safe access
pub static GLOBAL_CALC_POOL: Lazy<ThreadSafeCalcPool> = Lazy::new(|| {
    ThreadSafeCalcPool {
        calculators: Arc::new(Mutex::new(Vec::new())),
    }
});

impl ThreadSafeCalcPool {
    /// Gets or creates a calculator instance from the pool
    pub fn get_calc(&self) -> MinaCalcResult<Calc> {
        let mut calculators = self.calculators.lock();
        
        // Try to reuse an existing calculator
        if let Some(calc) = calculators.pop() {
            // Validate the calculator is still valid
            if calc.is_valid() {
                return Ok(calc);
            }
            // If invalid, continue to create a new one
        }
        
        // Create a new one if none available
        Calc::new()
    }
    
    /// Returns a calculator to the pool
    pub fn return_calc(&self, calc: Calc) {
        // Only return valid calculators to the pool
        if calc.is_valid() {
            let mut calculators = self.calculators.lock();
            calculators.push(calc);
        }
    }
    
    /// Gets a calculator from the global pool
    pub fn get_global_calc() -> MinaCalcResult<Calc> {
        GLOBAL_CALC_POOL.get_calc()
    }
    
    /// Returns a calculator to the global pool
    pub fn return_global_calc(calc: Calc) {
        GLOBAL_CALC_POOL.return_calc(calc);
    }
    
    /// Gets the current pool size
    pub fn pool_size(&self) -> usize {
        let calculators = self.calculators.lock();
        calculators.len()
    }
    
    /// Clears the pool (useful for cleanup)
    pub fn clear_pool(&self) {
        let mut calculators = self.calculators.lock();
        calculators.clear();
    }
    
    /// Pre-populates the pool with a specified number of calculators
    pub fn pre_populate(&self, count: usize) -> MinaCalcResult<()> {
        let mut calculators = self.calculators.lock();
        
        for _ in 0..count {
            let calc = Calc::new()?;
            calculators.push(calc);
        }
        
        Ok(())
    }
    
    /// Gets a calculator with timeout (useful for avoiding deadlocks)
    pub fn get_calc_with_timeout(&self, timeout_ms: u64) -> MinaCalcResult<Calc> {
        use std::time::{Duration, Instant};
        
        let start = Instant::now();
        let timeout = Duration::from_millis(timeout_ms);
        
        loop {
            match self.get_calc() {
                Ok(calc) => return Ok(calc),
                Err(e) => {
                    if start.elapsed() >= timeout {
                        return Err(MinaCalcError::InternalError(
                            format!("Timeout waiting for calculator: {}ms", timeout_ms)
                        ));
                    }
                    // Small delay before retry
                    std::thread::sleep(Duration::from_millis(1));
                }
            }
        }
    }
}
