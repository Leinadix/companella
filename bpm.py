import argparse
import json
import librosa
import numpy as np
import scipy.ndimage
import sys


def parse_arguments():
    parser = argparse.ArgumentParser(
        description="Analyze audio files to detect beat timestamps and calculate instant BPM values."
    )
    parser.add_argument(
        "audio_file",
        type=str,
        help="Path to the audio file to analyze"
    )
    parser.add_argument(
        "-o", "--output",
        type=str,
        default=None,
        help="Output file path (optional, prints to stdout if not specified)"
    )
    parser.add_argument(
        "-a", "--average",
        action="store_true",
        help="Also display the average BPM"
    )
    parser.add_argument(
        "-j", "--json",
        action="store_true",
        help="Output results in JSON format"
    )
    parser.add_argument(
        "--bpm-hint",
        type=float,
        default=None,
        help="Expected BPM hint to guide detection (e.g., 180)"
    )
    parser.add_argument(
        "--percussion",
        action="store_true",
        help="Use harmonic/percussive separation for cleaner beat detection"
    )
    parser.add_argument(
        "--tightness",
        type=float,
        default=100,
        help="Beat tracking tightness (higher = stricter grid, default: 100)"
    )
    parser.add_argument(
        "--no-offbeat",
        action="store_true",
        help="Disable offbeat/syncopation detection"
    )
    parser.add_argument(
        "--bpm-tolerance",
        type=float,
        default=2.0,
        help="BPM tolerance for consolidation - changes smaller than this are merged (default: 2.0)"
    )
    parser.add_argument(
        "--no-stabilize",
        action="store_true",
        help="Disable BPM stabilization (output raw beat-by-beat BPM)"
    )
    return parser.parse_args()


def load_audio(filepath):
    try:
        # Load at higher sample rate for better timing precision
        # 44100 Hz gives ~0.02ms resolution per sample
        y, sr = librosa.load(filepath, sr=44100, mono=True)
        return y, sr
    except Exception as e:
        print(f"Error loading audio file: {e}", file=sys.stderr)
        sys.exit(1)


def analyze_bpm(y, sr, bpm_hint=None, use_percussion=False, tightness=100, 
                detect_offbeats=True, stabilize=True, bpm_tolerance=2.0):
    # Use smaller hop_length for finer time resolution
    # 128 samples at 44100 Hz = ~2.9ms per frame
    hop_length = 128
    n_fft = 2048
    
    # Optionally separate percussive component for cleaner beat detection
    if use_percussion:
        y_harmonic, y_percussive = librosa.effects.hpss(y)
        y_analysis = y_percussive
    else:
        y_analysis = y
    
    # Compute multiple onset strength envelopes and combine them
    # This is more robust for different music types
    onset_env = compute_combined_onset_envelope(y_analysis, sr, hop_length)
    
    # Estimate tempo with or without hint
    if bpm_hint is not None:
        # Use prior tempo to guide detection
        prior = create_tempo_prior(bpm_hint)
        tempo = librosa.feature.tempo(
            onset_envelope=onset_env,
            sr=sr,
            hop_length=hop_length,
            prior=prior
        )[0]
    else:
        tempo = librosa.feature.tempo(
            onset_envelope=onset_env,
            sr=sr,
            hop_length=hop_length
        )[0]
    
    # Get beat tracking with the onset envelope
    _, beat_frames = librosa.beat.beat_track(
        onset_envelope=onset_env,
        sr=sr,
        hop_length=hop_length,
        bpm=tempo,
        tightness=tightness,
        trim=False
    )
    
    # Detect actual onsets for refinement
    onset_frames = librosa.onset.onset_detect(
        onset_envelope=onset_env,
        sr=sr,
        hop_length=hop_length,
        backtrack=True,  # Find actual onset start, not peak
        units='frames'
    )
    
    # Refine beat positions by snapping to nearest detected onset
    beat_frames_refined = refine_beats_to_onsets_advanced(
        onset_env, beat_frames, onset_frames, hop_length, sr
    )
    
    # Detect offbeat sections and insert phase-shifted timing points
    if detect_offbeats:
        beat_frames_final = detect_and_insert_offbeats(
            beat_frames_refined, onset_frames, onset_env, tempo, hop_length, sr
        )
    else:
        beat_frames_final = beat_frames_refined
    
    # Convert frames to time with sub-sample precision using parabolic interpolation
    beat_times = frames_to_time_precise(beat_frames_final, onset_env, hop_length, sr)
    
    # Calculate instantaneous BPM from beat intervals
    beat_intervals = np.diff(beat_times)
    beat_intervals = np.maximum(beat_intervals, 1e-6)
    bpms = 60.0 / beat_intervals
    
    # Stabilize BPM by consolidating small variations into stable sections
    if stabilize and len(beat_times) > 1:
        beat_times, bpms = stabilize_bpm_sections(beat_times, bpms, bpm_tolerance)
    
    return beat_times, bpms, tempo


def compute_combined_onset_envelope(y, sr, hop_length):
    """
    Compute a combined onset strength envelope using multiple methods.
    This is more robust for different types of music.
    """
    # Spectral flux onset (good for general music)
    onset_spectral = librosa.onset.onset_strength(
        y=y,
        sr=sr,
        hop_length=hop_length,
        aggregate=np.median,
        fmax=8000,  # Focus on frequencies where beats are prominent
        n_mels=128
    )
    
    # Energy-based onset (good for percussion)
    S = np.abs(librosa.stft(y, hop_length=hop_length))
    onset_energy = librosa.onset.onset_strength(
        S=S,
        sr=sr,
        hop_length=hop_length,
        aggregate=np.mean
    )
    
    # Normalize both envelopes
    onset_spectral = onset_spectral / (np.max(onset_spectral) + 1e-8)
    onset_energy = onset_energy / (np.max(onset_energy) + 1e-8)
    
    # Combine with weighted average (spectral is usually more reliable)
    combined = 0.7 * onset_spectral + 0.3 * onset_energy
    
    # Apply mild smoothing to reduce noise while preserving transients
    combined = scipy.ndimage.median_filter(combined, size=3)
    
    return combined


def create_tempo_prior(bpm_hint, spread=20):
    """
    Create a tempo prior distribution centered around the hint.
    """
    def prior(bpm):
        # Gaussian centered at bpm_hint with given spread
        return np.exp(-0.5 * ((bpm - bpm_hint) / spread) ** 2)
    return prior


def refine_beats_to_onsets_advanced(onset_env, beat_frames, onset_frames, hop_length, sr):
    """
    Advanced beat refinement using detected onsets.
    Snaps beats to the nearest actual onset for precise timing.
    """
    refined = np.copy(beat_frames).astype(float)
    
    # Calculate search window based on tempo (1/8 note at estimated tempo)
    avg_beat_interval = np.median(np.diff(beat_frames)) if len(beat_frames) > 1 else 100
    search_window = max(4, int(avg_beat_interval / 4))  # 1/4 beat window
    
    for i, frame in enumerate(beat_frames):
        # Find nearest detected onset
        if len(onset_frames) > 0:
            onset_distances = np.abs(onset_frames - frame)
            nearest_onset_idx = np.argmin(onset_distances)
            nearest_onset = onset_frames[nearest_onset_idx]
            
            # Only snap if onset is within search window
            if onset_distances[nearest_onset_idx] <= search_window:
                refined[i] = nearest_onset
                continue
        
        # Fallback: find local maximum in onset envelope
        start = max(0, int(frame) - search_window)
        end = min(len(onset_env), int(frame) + search_window + 1)
        
        if start < end:
            local_max = np.argmax(onset_env[start:end])
            refined[i] = start + local_max
    
    return refined


def detect_and_insert_offbeats(beat_frames, onset_frames, onset_env, tempo, hop_length, sr):
    """
    Detect sections where rhythm shifts to offbeats and insert timing points.
    
    Analyzes onset positions relative to the beat grid to find:
    - 1/2 beat offsets (syncopation)
    - 1/4 beat offsets (swing/shuffle)
    
    Inserts new beat markers at the start of offbeat sections.
    """
    if len(beat_frames) < 4 or len(onset_frames) < 4:
        return beat_frames
    
    beat_frames = np.array(beat_frames, dtype=float)
    onset_frames = np.array(onset_frames, dtype=float)
    
    # Calculate beat interval in frames
    beat_interval = np.median(np.diff(beat_frames))
    if beat_interval <= 0:
        return beat_frames
    
    # Analyze phase of each onset relative to the beat grid
    # Phase is 0.0 for on-beat, 0.5 for half-beat offset, etc.
    onset_phases = []
    onset_beat_indices = []
    
    for onset in onset_frames:
        # Find which beat interval this onset falls in
        beat_idx = np.searchsorted(beat_frames, onset) - 1
        if 0 <= beat_idx < len(beat_frames) - 1:
            beat_start = beat_frames[beat_idx]
            local_interval = beat_frames[beat_idx + 1] - beat_start
            if local_interval > 0:
                phase = (onset - beat_start) / local_interval
                # Normalize phase to [0, 1)
                phase = phase % 1.0
                onset_phases.append(phase)
                onset_beat_indices.append(beat_idx)
    
    if len(onset_phases) < 4:
        return beat_frames
    
    onset_phases = np.array(onset_phases)
    onset_beat_indices = np.array(onset_beat_indices)
    
    # Detect phase shifts by analyzing windows of onsets
    window_size = 8  # Analyze 8 onsets at a time
    phase_shifts = []
    
    # Define offbeat thresholds (phases that indicate offbeat patterns)
    # 0.5 = half-beat (syncopation), 0.25/0.75 = quarter-beat (swing)
    offbeat_phases = [0.5, 0.25, 0.75]
    phase_tolerance = 0.15  # Allow 15% deviation
    
    for i in range(len(onset_phases) - window_size):
        window_phases = onset_phases[i:i + window_size]
        window_beat_idx = onset_beat_indices[i:i + window_size]
        
        # Check if this window shows consistent offbeat pattern
        for offbeat_phase in offbeat_phases:
            # Count how many onsets are near this offbeat phase
            near_offbeat = np.abs(window_phases - offbeat_phase) < phase_tolerance
            offbeat_ratio = np.mean(near_offbeat)
            
            # If >60% of onsets are at this offbeat phase, it's an offbeat section
            if offbeat_ratio > 0.6:
                # Check if previous window was NOT at this phase (transition point)
                if i >= window_size:
                    prev_window_phases = onset_phases[i - window_size:i]
                    prev_near_offbeat = np.abs(prev_window_phases - offbeat_phase) < phase_tolerance
                    prev_ratio = np.mean(prev_near_offbeat)
                    
                    # Transition detected: previous section was on-beat, current is offbeat
                    if prev_ratio < 0.4:
                        transition_beat_idx = int(window_beat_idx[0])
                        phase_shifts.append({
                            'beat_idx': transition_beat_idx,
                            'phase': offbeat_phase,
                            'type': 'to_offbeat'
                        })
                else:
                    # Start of song is offbeat
                    if i == 0:
                        phase_shifts.append({
                            'beat_idx': 0,
                            'phase': offbeat_phase,
                            'type': 'start_offbeat'
                        })
        
        # Also detect transitions back to on-beat
        near_onbeat = onset_phases[i:i + window_size] < phase_tolerance
        near_onbeat |= onset_phases[i:i + window_size] > (1.0 - phase_tolerance)
        onbeat_ratio = np.mean(near_onbeat)
        
        if onbeat_ratio > 0.6 and i >= window_size:
            prev_window_phases = onset_phases[i - window_size:i]
            for offbeat_phase in offbeat_phases:
                prev_near_offbeat = np.abs(prev_window_phases - offbeat_phase) < phase_tolerance
                if np.mean(prev_near_offbeat) > 0.5:
                    transition_beat_idx = int(onset_beat_indices[i])
                    phase_shifts.append({
                        'beat_idx': transition_beat_idx,
                        'phase': 0.0,
                        'type': 'to_onbeat'
                    })
                    break
    
    # Remove duplicate phase shifts at same beat
    seen_beats = set()
    unique_shifts = []
    for shift in phase_shifts:
        if shift['beat_idx'] not in seen_beats:
            seen_beats.add(shift['beat_idx'])
            unique_shifts.append(shift)
    
    if not unique_shifts:
        return beat_frames
    
    # Insert new timing points at phase shifts
    new_beats = list(beat_frames)
    inserted_count = 0
    
    for shift in sorted(unique_shifts, key=lambda x: x['beat_idx']):
        beat_idx = shift['beat_idx']
        phase = shift['phase']
        
        if beat_idx < 0 or beat_idx >= len(beat_frames) - 1:
            continue
        
        if phase > 0.01:  # Not on-beat, need to insert offset timing point
            beat_start = beat_frames[beat_idx]
            local_interval = beat_frames[beat_idx + 1] - beat_start
            
            # Calculate the offbeat position
            offbeat_frame = beat_start + (phase * local_interval)
            
            # Find the actual onset nearest to this theoretical offbeat position
            onset_distances = np.abs(onset_frames - offbeat_frame)
            if len(onset_distances) > 0:
                nearest_idx = np.argmin(onset_distances)
                if onset_distances[nearest_idx] < local_interval * 0.2:
                    # Use actual onset position for precision
                    offbeat_frame = onset_frames[nearest_idx]
            
            # Insert at correct position in the list
            insert_pos = beat_idx + 1 + inserted_count
            new_beats.insert(insert_pos, offbeat_frame)
            inserted_count += 1
    
    # Sort and remove duplicates
    new_beats = sorted(set(new_beats))
    
    return np.array(new_beats)


def frames_to_time_precise(frames, onset_env, hop_length, sr):
    """
    Convert frame indices to time with sub-frame precision using parabolic interpolation.
    This gives more accurate timing than simple frame-to-time conversion.
    """
    times = []
    
    for frame in frames:
        frame_int = int(frame)
        
        # Use parabolic interpolation if we have neighboring frames
        if 1 <= frame_int < len(onset_env) - 1:
            alpha = onset_env[frame_int - 1]
            beta = onset_env[frame_int]
            gamma = onset_env[frame_int + 1]
            
            # Parabolic peak interpolation
            if beta > alpha and beta > gamma:
                # Peak is at frame_int, refine with parabola
                p = 0.5 * (alpha - gamma) / (alpha - 2 * beta + gamma + 1e-8)
                refined_frame = frame_int + p
            else:
                refined_frame = float(frame)
        else:
            refined_frame = float(frame)
        
        # Convert to time
        time = refined_frame * hop_length / sr
        times.append(time)
    
    return np.array(times)


def stabilize_bpm_sections(beat_times, bpms, tolerance=2.0):
    """
    Consolidate small BPM variations into stable sections.
    
    Instead of creating a timing point for every tiny BPM fluctuation,
    this groups consecutive beats with similar BPM into sections and
    outputs only one timing point per stable section.
    
    Args:
        beat_times: Array of beat timestamps
        bpms: Array of instantaneous BPM values
        tolerance: Maximum BPM difference to consider as "same tempo"
    
    Returns:
        Tuple of (stabilized_times, stabilized_bpms) with one entry per section
    """
    if len(bpms) < 2:
        return beat_times, bpms
    
    # Find sections of stable BPM
    sections = []
    current_section_start = 0
    current_section_bpms = [bpms[0]]
    
    for i in range(1, len(bpms)):
        current_median = np.median(current_section_bpms)
        
        # Check if this beat's BPM is within tolerance of current section
        if abs(bpms[i] - current_median) <= tolerance:
            # Same section, add to current
            current_section_bpms.append(bpms[i])
        else:
            # New section detected
            # Finalize current section
            section_bpm = np.median(current_section_bpms)
            sections.append({
                'start_idx': current_section_start,
                'end_idx': i - 1,
                'bpm': section_bpm,
                'time': beat_times[current_section_start]
            })
            
            # Start new section
            current_section_start = i
            current_section_bpms = [bpms[i]]
    
    # Don't forget the last section
    section_bpm = np.median(current_section_bpms)
    sections.append({
        'start_idx': current_section_start,
        'end_idx': len(bpms) - 1,
        'bpm': section_bpm,
        'time': beat_times[current_section_start]
    })
    
    # Merge adjacent sections with same BPM (after rounding)
    merged_sections = []
    for section in sections:
        if merged_sections:
            last = merged_sections[-1]
            # If BPMs are within tolerance, merge
            if abs(section['bpm'] - last['bpm']) <= tolerance:
                # Extend the last section
                last['end_idx'] = section['end_idx']
                # Recalculate BPM as weighted average
                last_count = last['end_idx'] - last['start_idx'] + 1
                curr_count = section['end_idx'] - section['start_idx'] + 1
                last['bpm'] = (last['bpm'] * last_count + section['bpm'] * curr_count) / (last_count + curr_count)
            else:
                merged_sections.append(section)
        else:
            merged_sections.append(section)
    
    # Build output arrays
    # Only output one timing point per section
    stabilized_times = []
    stabilized_bpms = []
    
    for i, section in enumerate(merged_sections):
        stabilized_times.append(section['time'])
        stabilized_bpms.append(section['bpm'])
        
        # Add the end time for the last section so we have proper intervals
        if i == len(merged_sections) - 1:
            # Add the final beat time
            stabilized_times.append(beat_times[section['end_idx'] + 1] if section['end_idx'] + 1 < len(beat_times) else beat_times[-1])
    
    return np.array(stabilized_times), np.array(stabilized_bpms)


def format_output(beat_times, bpms, show_average, tempo):
    lines = []
    lines.append("Time (s)     |  Instant BPM")
    lines.append("-" * 30)
    for t, bpm in zip(beat_times[:-1], bpms):
        # Show 3 decimal places for millisecond precision
        lines.append(f"{t:.3f}s     |  {bpm:.2f}")
    
    if show_average:
        lines.append("-" * 30)
        avg_bpm = np.mean(bpms)
        lines.append(f"Average BPM: {avg_bpm:.2f}")
        lines.append(f"Estimated Tempo: {float(tempo):.2f}")
    
    return "\n".join(lines)


def format_json_output(beat_times, bpms, show_average, tempo):
    beats = []
    for t, bpm in zip(beat_times[:-1], bpms):
        beats.append({
            # Use 4 decimal places for sub-millisecond precision
            # osu! timing points use milliseconds, so 3+ decimals is ideal
            "time": round(float(t), 4),
            "bpm": round(float(bpm), 2)
        })
    
    result = {
        "beats": beats
    }
    
    if show_average:
        result["average_bpm"] = round(float(np.mean(bpms)), 2)
        result["estimated_tempo"] = round(float(tempo), 2)
    
    return json.dumps(result, indent=2)


def main():
    args = parse_arguments()
    
    y, sr = load_audio(args.audio_file)
    beat_times, bpms, tempo = analyze_bpm(
        y, sr,
        bpm_hint=args.bpm_hint,
        use_percussion=args.percussion,
        tightness=args.tightness,
        detect_offbeats=not args.no_offbeat,
        stabilize=not args.no_stabilize,
        bpm_tolerance=args.bpm_tolerance
    )
    
    if args.json:
        output = format_json_output(beat_times, bpms, args.average, tempo)
    else:
        output = format_output(beat_times, bpms, args.average, tempo)
    
    if args.output:
        try:
            with open(args.output, "w") as f:
                f.write(output)
            print(f"Results written to: {args.output}")
        except Exception as e:
            print(f"Error writing to output file: {e}", file=sys.stderr)
            sys.exit(1)
    else:
        print(output)


if __name__ == "__main__":
    main()
