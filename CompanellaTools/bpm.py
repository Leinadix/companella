#!/usr/bin/env python3
import argparse
import json
import sys
from collections import deque

import librosa
import numpy as np
import scipy.ndimage


def parse_arguments():
    p = argparse.ArgumentParser(
        description=(
            "osu!-style timing point generator (Mode A):\n"
            "- Pick an initial BPM.\n"
            "- Generate a perfect beat grid.\n"
            "- Compare grid vs transients (onsets).\n"
            "- Insert discrete timing points only when needed:\n"
            "  * BPM change (drift slope)\n"
            "  * Offset reset (phase jump)\n"
            "No beat snapping, no BPM-from-diffs spam."
        )
    )
    p.add_argument("audio_file", type=str, help="Path to audio file")

    p.add_argument("-o", "--output", type=str, default=None, help="Write output to file")
    p.add_argument("-j", "--json", action="store_true", help="Output JSON")
    p.add_argument("-a", "--average", action="store_true", help="Show average BPM across timing points")

    p.add_argument("--bpm-hint", type=float, default=None, help="Expected BPM hint (e.g. 180)")
    p.add_argument("--percussion", action="store_true", help="Use HPSS percussive component")
    p.add_argument("--tightness", type=float, default=80, help="librosa beat_track tightness (default 80)")

    # Onset/grid matching
    p.add_argument("--hop-length", type=int, default=256, help="Hop length for onset envelope (default 256)")
    p.add_argument("--match-window-ms", type=float, default=40.0, help="Max distance to match onset to beat (ms)")

    # Decision windows
    p.add_argument("--decision-window", type=int, default=24, help="Matched beats to evaluate (default 24)")
    p.add_argument("--min-matches", type=int, default=14, help="Minimum matched beats needed (default 14)")
    p.add_argument("--persist", type=int, default=8, help="Require condition to persist this many evaluations (default 8)")

    # Offset reset (phase jump)
    p.add_argument("--offset-threshold-ms", type=float, default=18.0, help="Offset reset threshold (ms) (default 18)")

    # BPM change (drift)
    p.add_argument(
        "--drift-slope-ms-per-beat",
        type=float,
        default=1.2,
        help="If median error drifts by this many ms per beat -> BPM change (default 1.2)",
    )
    p.add_argument(
        "--bpm-min-change",
        type=float,
        default=0.35,
        help="Minimum BPM change to output a new BPM timing point (default 0.35)",
    )

    # Phase (beat-zero alignment)
    p.add_argument(
        "--phase-divisions",
        type=int,
        default=4,
        help="Test this many equally spaced phase shifts across one beat (default 4 => quarters)",
    )
    p.add_argument(
        "--phase-search-seconds",
        type=float,
        default=18.0,
        help="How many seconds from start to use for phase scoring (default 18)",
    )

    # Anti-spam
    p.add_argument("--min-gap-ms", type=float, default=600.0, help="Min time between timing points (ms) (default 600)")
    p.add_argument("--max-points", type=int, default=200, help="Hard cap timing points (default 200)")

    return p.parse_args()


def load_audio(path: str):
    try:
        y, sr = librosa.load(path, sr=44100, mono=True)
        if y.size == 0:
            raise ValueError("Empty audio.")
        return y, sr
    except Exception as e:
        print(f"Error loading audio: {e}", file=sys.stderr)
        sys.exit(1)


def compute_onset_env(y, sr, hop_length):
    onset_spectral = librosa.onset.onset_strength(
        y=y,
        sr=sr,
        hop_length=hop_length,
        aggregate=np.median,
        fmax=8000,
        n_mels=128,
    )
    S = np.abs(librosa.stft(y, n_fft=2048, hop_length=hop_length))
    onset_energy = librosa.onset.onset_strength(S=S, sr=sr, hop_length=hop_length, aggregate=np.mean)

    onset_spectral = onset_spectral / (np.max(onset_spectral) + 1e-12)
    onset_energy = onset_energy / (np.max(onset_energy) + 1e-12)
    env = 0.75 * onset_spectral + 0.25 * onset_energy
    env = scipy.ndimage.median_filter(env, size=3)
    return env


def create_tempo_prior(bpm_hint, spread=20.0):
    def prior(bpm):
        return np.exp(-0.5 * ((bpm - bpm_hint) / spread) ** 2)

    return prior


def estimate_initial_bpm(onset_env, sr, hop_length, bpm_hint):
    if bpm_hint is not None:
        prior = create_tempo_prior(bpm_hint)
        return float(librosa.feature.tempo(onset_envelope=onset_env, sr=sr, hop_length=hop_length, prior=prior)[0])
    return float(librosa.feature.tempo(onset_envelope=onset_env, sr=sr, hop_length=hop_length)[0])


def estimate_initial_anchor(onset_env, sr, hop_length, bpm, tightness):
    # Use beat_track ONLY to seed a starting time anchor (t0).
    _, beat_frames = librosa.beat.beat_track(
        onset_envelope=onset_env,
        sr=sr,
        hop_length=hop_length,
        bpm=bpm,
        tightness=tightness,
        trim=False,
    )
    if beat_frames is None or len(beat_frames) < 2:
        return 0.0
    beat_times = librosa.frames_to_time(beat_frames, sr=sr, hop_length=hop_length)
    return float(beat_times[0])


def find_nearest_onset(onset_times, idx_hint, t, window_s):
    """
    onset_times must be sorted.
    idx_hint is the previous onset index (monotonic scan).
    Returns (matched_time or None, new_idx_hint)
    """
    n = len(onset_times)
    if n == 0:
        return None, idx_hint

    i = max(0, idx_hint)
    while i + 1 < n and onset_times[i] < t - window_s:
        i += 1

    candidates = []
    for j in (i - 1, i, i + 1, i + 2):
        if 0 <= j < n:
            dt = abs(onset_times[j] - t)
            if dt <= window_s:
                candidates.append((dt, j))

    if not candidates:
        return None, i

    _, best_j = min(candidates, key=lambda x: x[0])
    return float(onset_times[best_j]), best_j


def robust_slope_s_per_beat(beat_indices, errors_s):
    """
    Slope estimate of error vs beat index.
    Returns slope in seconds/beat.
    """
    x = np.asarray(beat_indices, dtype=float)
    y = np.asarray(errors_s, dtype=float)
    if x.size < 6:
        return 0.0
    x0 = x - np.median(x)
    y0 = y - np.median(y)
    denom = np.dot(x0, x0)
    if denom <= 1e-12:
        return 0.0
    return float(np.dot(x0, y0) / denom)


def choose_best_phase(t0, interval, onset_times, match_window_s, search_seconds, phase_divisions):
    """
    Fix 'everything is on beat but beat-0 is shifted' by testing phase offsets:
      t0 + k/phase_divisions * interval

    Score: matched onsets count (primary), median absolute error (tie-breaker).
    """
    if onset_times.size == 0 or interval <= 1e-6:
        return t0

    phase_divisions = max(1, int(phase_divisions))
    search_seconds = max(3.0, float(search_seconds))

    end_t = t0 + search_seconds
    ot = onset_times[(onset_times >= max(0.0, t0 - 0.5)) & (onset_times <= end_t + 0.5)]
    if ot.size == 0:
        return t0

    best_t0 = t0
    best_matches = -1
    best_med_abs_err = float("inf")

    for k in range(phase_divisions):
        cand_t0 = t0 + (k / phase_divisions) * interval
        onset_idx_hint = 0
        beat_idx = 0
        t = cand_t0

        errs = []
        matches = 0

        while t <= cand_t0 + search_seconds:
            m, onset_idx_hint = find_nearest_onset(ot, onset_idx_hint, t, match_window_s)
            if m is not None:
                matches += 1
                errs.append(m - t)
            beat_idx += 1
            t = cand_t0 + beat_idx * interval

        if matches == 0:
            continue

        med_abs_err = float(np.median(np.abs(errs))) if errs else float("inf")

        if (matches > best_matches) or (matches == best_matches and med_abs_err < best_med_abs_err):
            best_matches = matches
            best_med_abs_err = med_abs_err
            best_t0 = cand_t0

    return best_t0


def analyze_mode_a(
    y,
    sr,
    bpm_hint=None,
    use_percussion=False,
    tightness=80,
    hop_length=256,
    match_window_ms=40.0,
    decision_window=24,
    min_matches=14,
    persist=8,
    offset_threshold_ms=18.0,
    drift_slope_ms_per_beat=1.2,
    bpm_min_change=0.35,
    phase_divisions=4,
    phase_search_seconds=18.0,
    min_gap_ms=600.0,
    max_points=200,
):
    if use_percussion:
        try:
            y_h, y_p = librosa.effects.hpss(y)
            y_use = y_p
        except Exception:
            y_use = y
    else:
        y_use = y

    onset_env = compute_onset_env(y_use, sr, hop_length)

    onset_frames = librosa.onset.onset_detect(
        onset_envelope=onset_env,
        sr=sr,
        hop_length=hop_length,
        backtrack=False,
        units="frames",
    )
    onset_times = librosa.frames_to_time(onset_frames, sr=sr, hop_length=hop_length)
    onset_times = np.asarray(onset_times, dtype=float)
    onset_times.sort()

    bpm = estimate_initial_bpm(onset_env, sr, hop_length, bpm_hint)
    if not np.isfinite(bpm) or bpm <= 1e-6:
        bpm = 120.0

    interval = 60.0 / bpm
    t0 = estimate_initial_anchor(onset_env, sr, hop_length, bpm, tightness)

    window_s = match_window_ms / 1000.0

    # Snap anchor once if close (safe init)
    if onset_times.size > 0:
        j0 = int(np.argmin(np.abs(onset_times - t0)))
        if abs(onset_times[j0] - t0) <= window_s * 2.5:
            t0 = float(onset_times[j0])

    # Phase selection (quarter-beat by default)
    t0 = choose_best_phase(
        t0=t0,
        interval=interval,
        onset_times=onset_times,
        match_window_s=window_s,
        search_seconds=phase_search_seconds,
        phase_divisions=phase_divisions,
    )

    duration = len(y) / sr

    # Timing points list of (time, bpm)
    points = [(t0, float(bpm))]
    last_point_time = t0

    # Rolling matched errors
    err_q = deque(maxlen=max(8, int(decision_window)))
    idx_q = deque(maxlen=max(8, int(decision_window)))

    onset_idx_hint = 0
    eval_streak_offset = 0
    eval_streak_drift = 0

    beat_idx = 0
    t = t0

    while t < 0:
        beat_idx += 1
        t = t0 + beat_idx * interval

    while t <= duration and len(points) < max_points:
        matched, onset_idx_hint = find_nearest_onset(onset_times, onset_idx_hint, t, window_s)

        if matched is not None:
            e = matched - t
            err_q.append(float(e))
            idx_q.append(int(beat_idx))

        if len(err_q) >= min_matches:
            errors = np.asarray(err_q, dtype=float)
            indices = np.asarray(idx_q, dtype=int)

            med_e = float(np.median(errors))
            slope = robust_slope_s_per_beat(indices, errors)  # s/beat

            med_e_ms = med_e * 1000.0
            slope_ms = slope * 1000.0

            is_drift = abs(slope_ms) >= float(drift_slope_ms_per_beat)
            is_jump = (abs(med_e_ms) >= float(offset_threshold_ms)) and (
                abs(slope_ms) < float(drift_slope_ms_per_beat) * 0.6
            )

            eval_streak_drift = eval_streak_drift + 1 if is_drift else 0
            eval_streak_offset = eval_streak_offset + 1 if is_jump else 0

            if eval_streak_drift >= persist:
                new_interval = interval + slope
                new_bpm = 60.0 / new_interval if new_interval > 1e-4 else bpm

                if np.isfinite(new_bpm) and abs(new_bpm - bpm) >= bpm_min_change:
                    anchor_time = matched if matched is not None else (t + med_e)
                    if (anchor_time - last_point_time) * 1000.0 >= min_gap_ms:
                        bpm = float(new_bpm)
                        interval = 60.0 / bpm
                        t0 = float(anchor_time) - beat_idx * interval

                        points.append((float(anchor_time), float(bpm)))
                        last_point_time = float(anchor_time)

                        err_q.clear()
                        idx_q.clear()
                        eval_streak_drift = 0
                        eval_streak_offset = 0

            elif eval_streak_offset >= persist:
                anchor_time = matched if matched is not None else (t + med_e)
                if (anchor_time - last_point_time) * 1000.0 >= min_gap_ms:
                    t0 = float(anchor_time) - beat_idx * interval

                    points.append((float(anchor_time), float(bpm)))
                    last_point_time = float(anchor_time)

                    err_q.clear()
                    idx_q.clear()
                    eval_streak_drift = 0
                    eval_streak_offset = 0

        beat_idx += 1
        t = t0 + beat_idx * interval

    return points, bpm


def format_text(points, show_average, tempo_seed):
    lines = []
    lines.append("Time (s)     |  BPM")
    lines.append("-" * 26)
    for t, bpm in points:
        lines.append(f"{t:.3f}s     |  {bpm:.2f}")
    if show_average and points:
        avg = float(np.mean([b for _, b in points]))
        lines.append("-" * 26)
        lines.append(f"Average BPM (timing points): {avg:.2f}")
        lines.append(f"Initial tempo seed: {float(tempo_seed):.2f}")
    return "\n".join(lines)


def format_json(points, show_average, tempo_seed):
    out = {
        "beats": [{"time": round(float(t), 4), "bpm": round(float(bpm), 2)} for t, bpm in points]
    }
    if show_average and points:
        out["average_bpm"] = round(float(np.mean([b for _, b in points])), 2)
        out["tempo_seed"] = round(float(tempo_seed), 2)
    return json.dumps(out, indent=2)


def main():
    args = parse_arguments()
    y, sr = load_audio(args.audio_file)

    points, tempo_seed = analyze_mode_a(
        y=y,
        sr=sr,
        bpm_hint=args.bpm_hint,
        use_percussion=args.percussion,
        tightness=args.tightness,
        hop_length=args.hop_length,
        match_window_ms=args.match_window_ms,
        decision_window=args.decision_window,
        min_matches=args.min_matches,
        persist=args.persist,
        offset_threshold_ms=args.offset_threshold_ms,
        drift_slope_ms_per_beat=args.drift_slope_ms_per_beat,
        bpm_min_change=args.bpm_min_change,
        phase_divisions=args.phase_divisions,
        phase_search_seconds=args.phase_search_seconds,
        min_gap_ms=args.min_gap_ms,
        max_points=args.max_points,
    )

    if args.json:
        txt = format_json(points, args.average, tempo_seed)
    else:
        txt = format_text(points, args.average, tempo_seed)

    if args.output:
        try:
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(txt)
        except Exception as e:
            print(f"Error writing output: {e}", file=sys.stderr)
            sys.exit(1)
    else:
        print(txt)


if __name__ == "__main__":
    main()
