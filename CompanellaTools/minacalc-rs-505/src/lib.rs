//! Rust bindings for MinaCalc C++ library
//! 
//! This crate provides safe Rust bindings for the MinaCalc rhythm game difficulty calculator.
//! 
//! ## Features
//! 
//! - `hashmap` (default): Provides HashMap conversion for MSD results
//! - `thread`: Provides thread-safe calculator pool
//! - `osu`: Provides osu! beatmap parsing and calculation

mod wrapper;
mod error;

// Include automatically generated bindings
include!(concat!(env!("OUT_DIR"), "/bindings.rs"));

// Re-export wrapper types
pub use wrapper::*;
pub use error::*;

// Feature-gated modules
#[cfg(feature = "hashmap")]
pub mod hashmap;

#[cfg(feature = "thread")]
pub mod thread;

#[cfg(feature = "osu")]
pub mod osu;

#[cfg(feature = "utils")]
pub mod utils;

// Re-export feature-gated modules
#[cfg(feature = "hashmap")]
pub use hashmap::*;

#[cfg(feature = "thread")]
pub use thread::*;

#[cfg(feature = "osu")]
pub use osu::*;

#[cfg(feature = "utils")]
pub use utils::*;
