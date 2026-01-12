use std::env;
use std::path::PathBuf;

fn main() {
    // Compiler le code C++
    let mut build = cc::Build::new();
    
    // Ajouter les fichiers source C++
    build.cpp(true)
          .file("c_code/API.cpp")
          .file("c_code/MinaCalc/MinaCalc.cpp")
          .include("c_code")
          .include("c_code/MinaCalc");
    
    // Détecter le compilateur et ajouter les flags appropriés
    let target = env::var("TARGET").unwrap_or_default();
    if target.contains("msvc") {
        // MSVC: utiliser /std:c++17 au lieu de -std=c++17
        build.flag("/std:c++17");
    } else {
        // GCC/Clang: utiliser -std=c++17
        build.flag("-std=c++17");
    }
    
    // Compiler la bibliothèque
    build.compile("minacalc");
    
    // Générer les bindings FFI
    let bindings = bindgen::Builder::default()
        .header("c_code/API.h")
        .clang_arg("-I/usr/include")
        .clang_arg("-I/usr/include/x86_64-linux-gnu")
        .clang_arg("-I/usr/lib/gcc/x86_64-linux-gnu/13/include")
        .parse_callbacks(Box::new(bindgen::CargoCallbacks::new()))
        .generate()
        .expect("Unable to generate bindings");
    
    // Écrire les bindings dans le répertoire de sortie
    let out_path = PathBuf::from(env::var("OUT_DIR").unwrap());
    bindings
        .write_to_file(out_path.join("bindings.rs"))
        .expect("Couldn't write bindings!");
    
    // Indiquer à Cargo de recompiler si les fichiers C++ changent
    println!("cargo:rerun-if-changed=c_code/API.h");
    println!("cargo:rerun-if-changed=c_code/API.cpp");
    println!("cargo:rerun-if-changed=c_code/NoteDataStructures.h");
    println!("cargo:rerun-if-changed=c_code/MinaCalc/MinaCalc.cpp");
    
    // Définir des types conditionnels pour unsigned long
    // println!("cargo:rustc-cfg=target_os=\"{}\"", env::var("CARGO_CFG_TARGET_OS").unwrap());
}

