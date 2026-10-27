#!/usr/bin/env python3
"""Synthesises AVAZarch's sound effects, voice and tunes into ../src/AVAZarch/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, saw, sine and noise voices. The voice
("Welcome to the Wonder Realm!", "Get ready!", the hero's yell) speaks through the formant synthesiser in speech.py,
the way the talking arcade games of the day spoke, so no recording or speech engine is needed. The title and stage
music is Richard Wagner's "Ride of the Valkyries" (1856), which is in the public domain, arranged here for chip
voices; the boss and bonus tunes and the jingles are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAZarch", "Sounds")
RNG = random.Random(1987)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def gain(part, g):
    return [v * g for v in part]


def lowpass(samples, cutoff):
    a = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    out, y = [], 0.0
    for v in samples:
        y += a * (v - y)
        out.append(y)
    return out


def echoed(dry, taps=((0.09, 0.3), (0.19, 0.15))):
    """A little room ring."""
    out = dry + [0.0] * int(RATE * 0.4)
    for delay, g in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def seq(bpm, notes, kind="pulse", g=0.55, release=0.6, vibrato=0.0, legato=1.0):
    """A line of (note, beats); '-' is a rest. Notes can be cut short (legato below 1) for a detached feel."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
            continue
        sounding = tone(d * legato, hz(note), kind=kind, release=release, gain=g, vibrato=vibrato, vib_rate=6)
        out.extend(sounding + silence(d - len(sounding) / RATE))
    return out


def fit(part, seconds):
    n = int(RATE * seconds)
    return (part + [0.0] * n)[:n]


def drums(bpm, beats, pattern):
    """A drum track: pattern is a string per beat - k kick, s snare, h hat, '.' nothing."""
    beat = 60.0 / bpm
    out = []
    for i in range(beats):
        c = pattern[i % len(pattern)]
        if c == "k":
            hit = tone(beat, 130, 45, "sine", attack=0.002, release=2.2, gain=0.9)
        elif c == "s":
            hit = mix(tone(beat, 2200, 900, "noise", attack=0.001, release=3.0, gain=0.45), tone(beat * 0.5, 220, 160, "tri", release=2, gain=0.3))
        elif c == "h":
            hit = tone(beat * 0.5, 7000, 6500, "noise", attack=0.001, release=4, gain=0.16)
        else:
            hit = []
        out.extend(fit(hit, beat))
    return out


# ───────────────────────────── the organ ─────────────────────────────

def organ(seconds, note, g=0.5, attack=0.012, release=0.25):
    """A pipe-organ-ish note: a few sine partials (8', 4', 2 2/3', 2') with a soft start and end."""
    f = hz(note)
    n = int(RATE * seconds)
    out = []
    for i in range(n):
        t = i / RATE
        env = min(1.0, t / attack) * min(1.0, (seconds - t) / max(1e-3, release * seconds))
        s = (math.sin(2 * math.pi * f * t) + 0.55 * math.sin(4 * math.pi * f * t) + 0.3 * math.sin(6 * math.pi * f * t)
             + 0.22 * math.sin(8 * math.pi * f * t) + 0.08 * math.sin(2 * math.pi * f * 1.002 * t))
        out.append(s * env * g)
    return out


def organ_line(bpm, notes, g=0.5):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
        elif isinstance(note, tuple):
            out.extend(mix(*[organ(d, n, g / len(note) * 1.6) for n in note]))
        else:
            out.extend(organ(d, note, g))
    return out


def toccata():
    """The famous opening of BWV 565: the mordent and fall, three times, each an octave lower, then the low D and the
    rolled diminished chord over it, resolving."""
    phrase = lambda o: [(f"A{o + 1}", 0.25), (f"G{o + 1}", 0.25), (f"A{o + 1}", 2.5), ("-", 0.5),
                        (f"G{o + 1}", 0.25), (f"F{o + 1}", 0.25), (f"E{o + 1}", 0.25), (f"D{o + 1}", 0.25),
                        (f"C#{o + 1}", 1.5), (f"D{o + 1}", 2.0), ("-", 1.0)]
    line = phrase(4) + phrase(3) + phrase(2)
    tune = organ_line(72, line, 0.5)
    # The pedal D, with the diminished seventh rolled up over it, then the D minor chord.
    roll = organ_line(72, [("C#3", 0.25), ("E3", 0.25), ("G3", 0.25), ("A#3", 0.25), ("C#4", 0.25), ("E4", 0.25), ("G4", 0.25), ("A#4", 2.0)], 0.32)
    pedal = organ(len(roll) / RATE, "D2", 0.45, release=0.05)
    chord = organ_line(72, [(("D3", "F3", "A3", "D4", "F4"), 4.0)], 0.5)
    chord = mix(chord, organ(len(chord) / RATE, "D2", 0.45))
    return echoed(cat(tune, mix(roll, pedal), chord, silence(1.2)), taps=((0.11, 0.35), (0.23, 0.2), (0.37, 0.1)))


def loop_noise(seconds, cutoff, wobble=0.0):
    """Rumbling noise that loops without a click: the last stretch is faded into the first."""
    n = int(RATE * seconds)
    raw = lowpass([RNG.uniform(-1, 1) for _ in range(n + RATE // 4)], cutoff)
    raw = lowpass(raw, cutoff * 1.5)
    k = RATE // 4
    body = raw[:n]
    for i in range(k):
        f = i / k
        body[i] = raw[n + i] * (1 - f) + raw[i] * f
    if wobble:
        body = [v * (1 + wobble * math.sin(2 * math.pi * 7 * i / RATE)) for i, v in enumerate(body)]
    return body


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Shoot.wav", mix(tone(0.11, 1500, 380, "square", attack=0.001, release=1.6, gain=0.6), tone(0.05, 6000, 3000, "noise", attack=0.001, release=2, gain=0.3)), 0.3)
    write("EnemyShot.wav", tone(0.28, 700, 160, "saw", attack=0.003, release=0.9, vibrato=0.1, vib_rate=28), 0.24)
    write("Hit.wav", mix(tone(0.1, 320, 140, "square", attack=0.001, release=1.5, gain=0.6), tone(0.07, 2200, 800, "noise", release=2, gain=0.4)), 0.32)
    write("Blast.wav", mix(lowpass(tone(0.7, 2800, 200, "noise", attack=0.001, release=1.1), 1800), tone(0.4, 120, 35, "sine", attack=0.002, release=1.1, gain=0.8)), 0.5)
    write("BigBlast.wav", cat(mix(lowpass(tone(1.6, 2400, 90, "noise", attack=0.002, release=0.7), 1300), tone(1.3, 80, 24, "saw", vibrato=0.2, vib_rate=9, release=0.7, gain=0.7)),
                              lowpass(tone(0.8, 500, 200, "noise", release=1.5, gain=0.25), 600)), 0.62)
    write("Spore.wav", tone(0.35, 300, 1100, "sine", attack=0.01, release=1.2, vibrato=0.05, vib_rate=14), 0.16)
    write("BombAway.wav", tone(1.1, 2200, 400, "sine", attack=0.02, release=0.3, vibrato=0.01, vib_rate=9), 0.2)
    write("Mutate.wav", mix(tone(0.6, 200, 1600, "square", vibrato=0.15, vib_rate=19, release=0.6, gain=0.5), tone(0.6, 1600, 200, "saw", vibrato=0.15, vib_rate=23, release=0.6, gain=0.4)), 0.3)
    write("Land.wav", mix(tone(0.22, 140, 70, "sine", attack=0.002, release=1.4), lowpass(tone(0.15, 1500, 400, "noise", release=2, gain=0.4), 1200)), 0.42)
    write("Refuel.wav", tone(0.06, 1320, kind="pulse", release=0.8), 0.18)
    write("FuelLow.wav", cat(tone(0.12, 880, kind="square", release=0.2), silence(0.06), tone(0.12, 660, kind="square", release=0.2)), 0.22)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["D6", "F6", "A6", "D7", "F7", "A7"]], tone(0.3, hz("D7"), kind="pulse", release=0.6)), 0.3)
    write("Thrust.wav", loop_noise(1.6, 260, wobble=0.12), 0.55)
    write("WaveStart.wav", echoed(seq(150, [("D4", 0.5), ("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("E5", 0.5), ("A5", 1.5)], kind="tri", g=0.5, release=0.4)), 0.34)
    write("WaveClear.wav", echoed(mix(seq(160, [("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 0.5), ("-", 0.25), ("C#6", 0.25), ("D6", 2.5)], kind="pulse", g=0.5),
                                      seq(160, [("D3", 2), ("A2", 1), ("D3", 2)], kind="tri", g=0.5, release=0.3))), 0.34)
    write("GameOver.wav", echoed(organ_line(56, [("A3", 1), ("F3", 1), ("D3", 1), ("C#3", 1), (("D3", "F3", "A3"), 4)], 0.5)), 0.4)
    write("TitleTune.wav", toccata(), 0.42)


if __name__ == "__main__":
    main()
