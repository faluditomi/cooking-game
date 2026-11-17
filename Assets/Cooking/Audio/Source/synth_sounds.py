"""
Synthesizes the cooking prototype's sound effects and ambience as WAV files.

Everything is made from sine waves and filtered noise, so the sounds are ours (no samples, no
licences). Run it from anywhere with plain Python 3 (no packages needed):

    python synth_sounds.py                  (all of them)
    python synth_sounds.py ambience_loop    (just these)

It writes the clips next to this folder, in Assets/Cooking/Audio. Each sound is one function below;
tweak and re-run. The loops (sizzle, boil, ambience) cross-fade their ends so they repeat cleanly.
"""
import math
import os
import random
import struct
import wave

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
TAU = 2.0 * math.pi


# ---------------------------------------------------------------- building blocks

def silence(seconds, sr):
    return [0.0] * int(seconds * sr)


def noise(n, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def lowpass(x, cutoff, sr):
    a = math.exp(-TAU * cutoff / sr)
    y, out = 0.0, []
    for v in x:
        y = (1.0 - a) * v + a * y
        out.append(y)
    return out


def highpass(x, cutoff, sr):
    low = lowpass(x, cutoff, sr)
    return [v - l for v, l in zip(x, low)]


def bandpass(x, lo, hi, sr):
    return lowpass(highpass(x, lo, sr), hi, sr)


def tone(freq, seconds, sr, decay=None, attack=0.002, phase=0.0, sweep_to=None):
    """A sine, optionally sweeping to another pitch, with a short attack and an exponential decay."""
    n = int(seconds * sr)
    out, ph = [], phase
    for i in range(n):
        t = i / sr
        f = freq if sweep_to is None else freq + (sweep_to - freq) * (i / max(1, n - 1))
        ph += TAU * f / sr
        env = min(1.0, t / attack) if attack > 0 else 1.0
        if decay:
            env *= math.exp(-t / decay)
        out.append(math.sin(ph) * env)
    return out


def envelope(x, sr, attack=0.005, decay=None, release=0.01):
    n = len(x)
    out = []
    for i, v in enumerate(x):
        t = i / sr
        e = min(1.0, t / attack) if attack > 0 else 1.0
        if decay:
            e *= math.exp(-t / decay)
        tail = (n - i) / sr
        if tail < release:
            e *= tail / release
        out.append(v * e)
    return out


def mix(*tracks, length=None):
    n = length or max(len(t) for t in tracks)
    out = [0.0] * n
    for t in tracks:
        for i in range(min(n, len(t))):
            out[i] += t[i]
    return out


def place(base, sound, at_seconds, sr, gain=1.0):
    start = int(at_seconds * sr)
    for i, v in enumerate(sound):
        j = start + i
        if 0 <= j < len(base):
            base[j] += v * gain
    return base


def scale(x, g):
    return [v * g for v in x]


def bell(freqs, decays, seconds, sr, gains=None):
    """Inharmonic partials decaying at their own rates: bells, plates, coins."""
    gains = gains or [1.0] * len(freqs)
    parts = [scale(tone(f, seconds, sr, decay=d, attack=0.001), g) for f, d, g in zip(freqs, decays, gains)]
    return mix(*parts)


def loopable(x, sr, fade_seconds):
    """Cross-fades the last stretch into the start so the clip repeats without a seam."""
    f = int(fade_seconds * sr)
    body = x[:len(x) - f]
    for i in range(f):
        w = i / f
        body[i] = body[i] * w + x[len(x) - f + i] * (1.0 - w)
    return body


def save(name, x, sr, peak=0.85):
    top = max(1e-9, max(abs(v) for v in x))
    g = peak / top
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v * g)) * 32767)) for v in x))
    print("wrote", os.path.relpath(path, OUT), "%.2fs" % (len(x) / sr))


# ---------------------------------------------------------------- the sounds

SR = 44100
LOOP_SR = 22050


def ui_click(rng):
    t = tone(1900, 0.045, SR, decay=0.008)
    tick = envelope(bandpass(noise(int(0.045 * SR), rng), 2000, 7000, SR), SR, attack=0.0005, decay=0.004)
    return mix(t, scale(tick, 0.5))


def pickup(rng):
    whoosh = envelope(bandpass(noise(int(0.14 * SR), rng), 700, 3000, SR), SR, attack=0.05, decay=0.05)
    rise = tone(320, 0.14, SR, decay=0.06, attack=0.01, sweep_to=640)
    return mix(scale(whoosh, 0.7), scale(rise, 0.5))


def place_down(rng):
    thud = tone(150, 0.16, SR, decay=0.045, attack=0.001, sweep_to=110)
    knock = envelope(lowpass(noise(int(0.16 * SR), rng), 1200, SR), SR, attack=0.0005, decay=0.015)
    return mix(thud, scale(knock, 0.8))


def plate_clink(rng):
    ring = bell([1260, 2740, 4130, 5620], [0.22, 0.14, 0.09, 0.06], 0.5, SR, [1.0, 0.6, 0.4, 0.25])
    tap = envelope(highpass(noise(int(0.5 * SR), rng), 3000, SR), SR, attack=0.0003, decay=0.004)
    return mix(ring, scale(tap, 0.6))


def chop(rng):
    knock = bell([640, 1380], [0.035, 0.02], 0.13, SR, [1.0, 0.5])
    snap = envelope(bandpass(noise(int(0.13 * SR), rng), 1500, 5000, SR), SR, attack=0.0003, decay=0.008)
    return mix(knock, scale(snap, 1.2))


def peel(rng):
    n = int(0.2 * SR)
    scrape = bandpass(noise(n, rng), 2200, 6500, SR)
    scrape = [v * (0.6 + 0.4 * math.sin(TAU * 38 * i / SR)) for i, v in enumerate(scrape)]
    return envelope(scrape, SR, attack=0.04, release=0.08)


def grate(rng):
    n = int(0.24 * SR)
    rasp = highpass(noise(n, rng), 1500, SR)
    # The teeth: a fast saw-like chop of the noise.
    rasp = [v * (0.25 + 0.75 * ((i * 70.0 / SR) % 1.0)) for i, v in enumerate(rasp)]
    return envelope(rasp, SR, attack=0.02, release=0.06)


def prep_done(rng):
    a = mix(tone(880, 0.35, SR, decay=0.12), scale(tone(1760, 0.35, SR, decay=0.06), 0.25))
    b = mix(tone(1318, 0.45, SR, decay=0.16), scale(tone(2636, 0.45, SR, decay=0.07), 0.25))
    out = silence(0.55, SR)
    place(out, a, 0.0, SR)
    place(out, b, 0.09, SR)
    return out


def shake(rng):
    out = silence(0.32, SR)
    whoosh = envelope(bandpass(noise(int(0.3 * SR), rng), 300, 2200, SR), SR, attack=0.06, decay=0.1)
    place(out, scale(whoosh, 0.8), 0.0, SR)
    for _ in range(4):
        clink = bell([2200 + rng.uniform(-200, 200), 3500 + rng.uniform(-300, 300)], [0.03, 0.02], 0.08, SR, [0.5, 0.3])
        place(out, clink, rng.uniform(0.04, 0.22), SR)
    crackle = envelope(highpass(noise(int(0.2 * SR), rng), 3000, SR), SR, attack=0.01, decay=0.06)
    place(out, scale(crackle, 0.4), 0.08, SR)
    return out


def stir(rng):
    return mix(tone(1046, 0.18, SR, decay=0.06), scale(tone(2093, 0.18, SR, decay=0.03), 0.3))


def too_early(rng):
    return mix(tone(240, 0.2, SR, decay=0.07, sweep_to=170), scale(tone(480, 0.2, SR, decay=0.04, sweep_to=340), 0.3))


def burn(rng):
    n = int(0.55 * SR)
    hiss = envelope(highpass(noise(n, rng), 2800, SR), SR, attack=0.01, decay=0.2)
    buzz = [math.copysign(1.0, math.sin(TAU * 90 * i / SR)) * math.exp(-i / SR / 0.12) * 0.25 for i in range(int(0.35 * SR))]
    buzz = lowpass(buzz, 900, SR)
    return mix(hiss, buzz)


def sizzle_loop(rng):
    n = int(4.6 * LOOP_SR)
    base = lowpass(highpass(noise(n, rng), 1800, LOOP_SR), 8000, LOOP_SR)
    # Slow flutter, and fat crackles popping at random.
    flutter = lowpass([rng.uniform(0.5, 1.0) for _ in range(n)], 3, LOOP_SR)
    out = [b * (0.4 + 0.6 * f) for b, f in zip(base, flutter)]
    t = 0.0
    while t < 4.6:
        t += rng.expovariate(28)
        pop = envelope(highpass(noise(int(0.012 * LOOP_SR), rng), 2500, LOOP_SR), LOOP_SR, attack=0.0002, decay=0.003)
        place(out, scale(pop, rng.uniform(0.8, 2.2)), t, LOOP_SR)
    return loopable(out, LOOP_SR, 0.6)


def boil_loop(rng):
    n = int(4.6 * LOOP_SR)
    rumble = lowpass(noise(n, rng), 260, LOOP_SR)
    rumble = scale(rumble, 3.0)
    out = list(rumble)
    t = 0.0
    while t < 4.6:
        t += rng.expovariate(22)
        f = rng.uniform(220, 520)
        bubble = tone(f, 0.05, LOOP_SR, decay=0.015, attack=0.002, sweep_to=f * 2.1)
        place(out, scale(bubble, rng.uniform(0.15, 0.4)), t, LOOP_SR)
    return loopable(out, LOOP_SR, 0.6)


def serve(rng):
    out = place(silence(0.9, SR), place_down(rng), 0.0, SR)
    ding = bell([1568, 2350, 3920], [0.3, 0.18, 0.08], 0.8, SR, [1.0, 0.5, 0.2])
    return place(out, scale(ding, 0.7), 0.06, SR)


def coins(rng):
    out = silence(0.55, SR)
    for _ in range(6):
        c = bell([3400 * rng.uniform(0.9, 1.1), 5200 * rng.uniform(0.9, 1.1), 7000 * rng.uniform(0.9, 1.1)],
                 [0.09, 0.06, 0.04], 0.2, SR, [1.0, 0.6, 0.3])
        place(out, c, rng.uniform(0.0, 0.32), SR, gain=rng.uniform(0.5, 1.0))
    return out


def no_pay(rng):
    out = silence(0.55, SR)
    for k, f in enumerate([330, 247]):
        sq = [math.copysign(1.0, math.sin(TAU * f * i / SR)) * math.exp(-i / SR / 0.1) for i in range(int(0.25 * SR))]
        place(out, scale(lowpass(sq, 1200, SR), 0.5), k * 0.16, SR)
    return out


def dream(rng):
    out = silence(1.6, SR)
    for k, f in enumerate([659, 830, 988, 1318, 1661]):
        note = mix(tone(f, 1.0, SR, decay=0.35), scale(tone(f * 2, 1.0, SR, decay=0.15), 0.2))
        place(out, note, k * 0.09, SR)
    # A little room.
    echo = [0.0] * len(out)
    d = int(0.11 * SR)
    for i in range(d, len(out)):
        echo[i] = out[i - d] * 0.3
    return mix(out, echo)


def arrive(rng):
    out = silence(1.2, SR)
    place(out, bell([1046, 2093], [0.35, 0.12], 0.9, SR, [1.0, 0.25]), 0.0, SR)
    place(out, bell([784, 1568], [0.45, 0.15], 1.0, SR, [1.0, 0.25]), 0.18, SR)
    return out


def blip(rng):
    t = tone(1350, 0.03, SR, decay=0.008)
    tick = envelope(highpass(noise(int(0.03 * SR), rng), 3000, SR), SR, attack=0.0003, decay=0.003)
    return mix(scale(t, 0.6), scale(tick, 0.3))


def register(rng):
    out = silence(0.9, SR)
    clack = mix(tone(300, 0.08, SR, decay=0.02), scale(envelope(lowpass(noise(int(0.08 * SR), rng), 2500, SR), SR, attack=0.0003, decay=0.01), 1.2))
    place(out, clack, 0.0, SR)
    place(out, clack, 0.07, SR, gain=0.7)
    place(out, bell([2093, 3136, 4186], [0.35, 0.2, 0.1], 0.7, SR, [1.0, 0.6, 0.3]), 0.16, SR, gain=0.8)
    return out


def day_end(rng):
    return bell([110, 247, 413, 588, 870], [1.4, 0.9, 0.6, 0.4, 0.25], 2.6, SR, [1.0, 0.7, 0.5, 0.35, 0.2])


def resonator(x, freq, bandwidth, sr):
    """A two-pole resonant band-pass: one formant of a voice, the ring of a pipe."""
    r = math.exp(-math.pi * bandwidth / sr)
    c = 2.0 * r * math.cos(TAU * freq / sr)
    g = 1.0 - r
    y1 = y2 = 0.0
    out = []
    for v in x:
        y = g * v + c * y1 - r * r * y2
        y2, y1 = y1, y
        out.append(y)
    return out


def saw(f0_curve, sr):
    """A sawtooth following a pitch curve (one frequency per sample): the buzz of a voice or an engine."""
    ph, out = 0.0, []
    for f in f0_curve:
        ph = (ph + f / sr) % 1.0
        out.append(2.0 * ph - 1.0)
    return out


VOWELS = [(800, 1200), (500, 1800), (320, 2300), (500, 900), (360, 800), (650, 1700)]


def voice_phrase(rng, sr):
    """A few syllables of someone talking: a buzzing source shaped by vowel formants, pitch rising and falling."""
    base = rng.uniform(100, 230)
    syllables = rng.randint(2, 6)
    out = []
    for k in range(syllables):
        length = int(rng.uniform(0.09, 0.22) * sr)
        f1, f2 = rng.choice(VOWELS)
        start = base * rng.uniform(0.9, 1.15)
        end = start * rng.uniform(0.85, 1.12)
        curve = [start + (end - start) * i / length for i in range(length)]
        buzz = saw(curve, sr)
        sound = mix(resonator(buzz, f1, 90, sr), scale(resonator(buzz, f2, 130, sr), 0.6))
        sound = envelope(sound, sr, attack=0.015, release=0.04)
        out.extend(sound)
        out.extend([0.0] * int(rng.uniform(0.01, 0.06) * sr))
    return out


def reverb(x, sr, mix_amount=0.35):
    """A small Schroeder reverb: four combs into two all-passes. Makes it a street, not a booth."""
    def comb(sig, delay, fb):
        d = int(delay * sr)
        buf = [0.0] * len(sig)
        for i in range(len(sig)):
            buf[i] = sig[i] + (buf[i - d] * fb if i >= d else 0.0)
        return buf

    def allpass(sig, delay, g):
        d = int(delay * sr)
        out = [0.0] * len(sig)
        for i in range(len(sig)):
            delayed = sig[i - d] if i >= d else 0.0
            prev = out[i - d] if i >= d else 0.0
            out[i] = -g * sig[i] + delayed + g * prev
        return out

    wet = mix(comb(x, 0.0297, 0.78), comb(x, 0.0371, 0.76), comb(x, 0.0411, 0.74), comb(x, 0.0437, 0.72))
    wet = allpass(allpass(wet, 0.005, 0.7), 0.0017, 0.7)
    wet = lowpass(wet, 3500, sr)
    return [d * (1.0 - mix_amount) + w * mix_amount * 0.25 for d, w in zip(x, wet)]


def ambience_loop(rng):
    """
    The Bright Market at night: a busy sci-fi food street. Talk from the crowd and the stalls, hover
    traffic passing, a soft synth drone over the city, plates and cutlery, the odd till and a sizzle
    from the stall next door, a buzzing neon sign and a PA chime.
    """
    seconds = 30.0
    sr = LOOP_SR
    n = int(seconds * sr)
    out = [0.0] * n

    # The city's tone: a low, slowly breathing synth chord instead of a noise bed.
    for f, g in [(55.0, 0.5), (82.4, 0.3), (110.0, 0.3), (164.8, 0.18), (246.9, 0.1)]:
        wobble = rng.uniform(0.05, 0.12)
        detune = rng.uniform(-0.3, 0.3)
        for i in range(n):
            t = i / sr
            out[i] += math.sin(TAU * (f + detune) * t) * g * 0.2 * (0.75 + 0.25 * math.sin(TAU * wobble * t + f))

    # Chatter comes and goes: a short exchange between two or three people passing by or at a
    # stall, then a quiet stretch, then another somewhere else.
    crowd = [0.0] * n
    t = rng.uniform(0.5, 2.0)
    while t < seconds - 2.0:
        near = rng.random() < 0.25
        gain = rng.uniform(0.5, 0.85) if near else rng.uniform(0.18, 0.4)
        cutoff = 3200 if near else 1500
        speakers = [rng.uniform(0.85, 1.2) for _ in range(rng.randint(1, 3))]
        lines = rng.randint(2, 5)
        at = t
        for _ in range(lines):
            phrase = voice_phrase(rng, sr)
            # Each speaker sounds a little different.
            pitch = rng.choice(speakers)
            if pitch != 1.0:
                phrase = [phrase[int(i * pitch)] for i in range(int(len(phrase) / pitch))]
            place(crowd, scale(lowpass(phrase, cutoff, sr), gain), at, sr)
            at += len(phrase) / sr + rng.uniform(0.15, 0.6)
        # Then a gap before the next bit of talk.
        t = at + rng.uniform(2.0, 5.5)
    out = mix(out, scale(crowd, 2.5))

    # Hover traffic going past: an engine hum that swells, falls in pitch, and fades.
    t = rng.uniform(0.5, 3.0)
    while t < seconds - 2.0:
        length = rng.uniform(3.0, 5.5)
        m = int(length * sr)
        f0 = rng.uniform(70, 140)
        curve = [f0 * (1.06 - 0.12 * (i / m)) for i in range(m)]
        engine = mix(resonator(saw(curve, sr), f0 * 3, 200, sr), scale(lowpass(noise(m, rng), 500, sr), 0.6))
        swell = [math.sin(math.pi * i / m) ** 2 for i in range(m)]
        engine = lowpass([e * s for e, s in zip(engine, swell)], 1400, sr)
        place(out, scale(engine, rng.uniform(0.07, 0.14)), t, sr)
        t += rng.uniform(3.0, 7.0)

    # Plates, bowls and cutlery from the stalls around.
    t = 0.0
    while t < seconds:
        t += rng.expovariate(2.4)
        if rng.random() < 0.6:
            clink = bell([rng.uniform(2200, 3200), rng.uniform(4300, 5600)], [0.08, 0.05], 0.25, sr, [1.0, 0.5])
        else:
            clink = bell([rng.uniform(900, 1400), rng.uniform(2500, 3000)], [0.15, 0.08], 0.35, sr, [1.0, 0.4])
        place(out, scale(lowpass(clink, 4500, sr), rng.uniform(0.06, 0.16)), t, sr)
        if rng.random() < 0.3:
            place(out, scale(lowpass(clink, 4500, sr), rng.uniform(0.03, 0.08)), t + rng.uniform(0.05, 0.2), sr)

    # The stall next door frying something, now and then.
    for _ in range(3):
        m = int(rng.uniform(1.5, 3.0) * sr)
        fry = bandpass(noise(m, rng), 2000, 6000, sr)
        fry = [v * math.sin(math.pi * i / m) for i, v in enumerate(fry)]
        place(out, scale(fry, 0.05), rng.uniform(1.0, seconds - 4.0), sr)

    # Somebody's till.
    for _ in range(2):
        ding = bell([2093, 3136], [0.3, 0.15], 0.6, sr, [1.0, 0.5])
        place(out, scale(lowpass(ding, 3000, sr), 0.07), rng.uniform(2.0, seconds - 3.0), sr)

    # A neon sign buzzing on and off.
    m = int(seconds * sr)
    buzz = lowpass([math.copysign(1.0, math.sin(TAU * 120 * i / sr)) for i in range(m)], 600, sr)
    gate = lowpass([1.0 if rng.random() < 0.6 else 0.0 for _ in range(m // 2205 + 1) for _ in range(2205)][:m], 20, sr)
    out = mix(out, [b * g * 0.012 for b, g in zip(buzz, gate)])

    # The station's PA: a three-note chime, far off.
    pa = silence(2.5, sr)
    for k, f in enumerate([784, 659, 523]):
        place(pa, mix(tone(f, 1.4, sr, decay=0.5), scale(tone(f * 2, 1.4, sr, decay=0.2), 0.2)), k * 0.35, sr)
    place(out, scale(lowpass(pa, 2500, sr), 0.08), rng.uniform(8.0, 18.0), sr)

    out = reverb(out, sr, 0.4)
    return loopable(out, sr, 2.5)


SOUNDS = [
    ("ui_click", ui_click, SR), ("pickup", pickup, SR), ("place", place_down, SR), ("plate", plate_clink, SR),
    ("chop", chop, SR), ("peel", peel, SR), ("grate", grate, SR), ("prep_done", prep_done, SR),
    ("shake", shake, SR), ("stir", stir, SR), ("too_early", too_early, SR), ("burn", burn, SR),
    ("sizzle_loop", sizzle_loop, LOOP_SR), ("boil_loop", boil_loop, LOOP_SR),
    ("serve", serve, SR), ("coins", coins, SR), ("no_pay", no_pay, SR), ("dream", dream, SR),
    ("arrive", arrive, SR), ("blip", blip, SR), ("register", register, SR), ("day_end", day_end, SR),
    ("ambience_loop", ambience_loop, LOOP_SR),
]

if __name__ == "__main__":
    import sys
    wanted = set(sys.argv[1:])  # names to make; none makes them all
    for name, make, sr in SOUNDS:
        if wanted and name not in wanted:
            continue
        # A fixed seed per sound: re-running gives the same files.
        save(name, make(random.Random(name)), sr, peak=0.6 if name.endswith("_loop") else 0.85)
