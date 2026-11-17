using System;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>
    /// Sound effects and the market ambience (the music is coming separately). The clips are made by
    /// Assets/Cooking/Audio/Source/synth_sounds.py and assigned by Custom Tools → Cooking Prototype →
    /// Assign Sounds. One-shots play from a small pool of sources with a little pitch variation; the
    /// sizzle and boil loops follow the pan and pot, and the ambience runs all the time.
    /// </summary>
    public partial class CookingPrototype
    {
        [Serializable]
        public class SoundSet
        {
            public AudioClip uiClick;
            public AudioClip pickup;
            public AudioClip place;
            public AudioClip plate;
            public AudioClip chop;
            public AudioClip peel;
            public AudioClip grate;
            public AudioClip prepDone;
            public AudioClip shake;
            public AudioClip stir;
            public AudioClip tooEarly;
            public AudioClip burn;
            public AudioClip sizzle;
            public AudioClip boil;
            public AudioClip serve;
            public AudioClip coins;
            public AudioClip noPay;
            public AudioClip dream;
            public AudioClip arrive;
            public AudioClip blip;
            public AudioClip register;
            public AudioClip dayEnd;
            public AudioClip ambience;
        }

        [Header("Sound")]
        [SerializeField] private SoundSet sounds = new SoundSet();
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.35f;
        [Tooltip("The pan's sizzle and the pot's bubbling, at full.")]
        [SerializeField, Range(0f, 1f)] private float cookingVolume = 0.45f;

        private const int SfxVoices = 8;
        private const float BlipEvery = 0.05f;

        private AudioSource[] sfxSources;
        private int nextSfx;
        private AudioSource sizzleSource;
        private AudioSource boilSource;
        private AudioSource ambienceSource;
        private float lastBlip;
        private int lastBurnt;

        /// <summary>Sets the clips. Used by the editor's Assign Sounds menu.</summary>
        public void SetSounds(SoundSet set)
        {
            sounds = set;
        }

        private void InitAudio()
        {
            if(FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

            sfxSources = new AudioSource[SfxVoices];

            for(int i = 0; i < SfxVoices; i++)
            {
                sfxSources[i] = gameObject.AddComponent<AudioSource>();
                sfxSources[i].playOnAwake = false;
            }

            sizzleSource = LoopSource(sounds.sizzle);
            boilSource = LoopSource(sounds.boil);
            ambienceSource = LoopSource(sounds.ambience);
            ambienceSource.volume = ambienceVolume;
            if(ambienceSource.clip != null) ambienceSource.Play();

            // Every button clicks.
            UIFactory.Clicked = () => Play(sounds.uiClick, 0.5f, 0.03f);
        }

        private AudioSource LoopSource(AudioClip clip)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            return source;
        }

        /// <summary>A one-shot, slightly re-pitched each time so repeats don't sound mechanical.</summary>
        private void Play(AudioClip clip, float volume = 1f, float pitchJitter = 0.06f, float pitch = 1f)
        {
            if(clip == null || sfxSources == null) return;

            AudioSource source = sfxSources[nextSfx];
            nextSfx = (nextSfx + 1) % sfxSources.Length;
            source.pitch = pitch * (1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter));
            source.PlayOneShot(clip, volume * sfxVolume);
        }

        /// <summary>Sizzle while the pan fries on the heat, bubbling while the pot boils; both fade in and out.</summary>
        private void UpdateCookingSounds()
        {
            if(sizzleSource == null) return;

            bool live = phase == Phase.Service && !overlayOpen;
            Fade(sizzleSource, live && wok.IsCooking && heldStation != wokView ? cookingVolume * (0.6f + 0.4f * wok.Progress) : 0f);
            Fade(boilSource, live && pot.IsCooking && heldStation != potView ? cookingVolume * 0.8f : 0f);
            if(ambienceSource != null) ambienceSource.volume = ambienceVolume;

            // Something in the pan just burnt.
            if(wok.BurntCount > lastBurnt) Play(sounds.burn, 0.7f);
            lastBurnt = wok.IsCooking ? wok.BurntCount : 0;
        }

        private static void Fade(AudioSource source, float target)
        {
            if(source.clip == null) return;

            source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 1.5f);

            if(source.volume > 0.001f && !source.isPlaying) source.Play();
            else if(source.volume <= 0.001f && source.isPlaying) source.Stop();
        }

        /// <summary>A soft blip as a line types out, at most every few letters.</summary>
        private void TypeBlip()
        {
            if(Time.unscaledTime - lastBlip < BlipEvery) return;
            lastBlip = Time.unscaledTime;
            Play(sounds.blip, 0.25f, 0.12f);
        }

        private void PlayToolStroke(PrepSteps tool)
        {
            AudioClip clip = tool == PrepSteps.Sliced ? sounds.chop : tool == PrepSteps.Peeled ? sounds.peel : sounds.grate;
            Play(clip, 0.8f, 0.1f);
        }

        /// <summary>The verdict, heard: a dream meal's chime, coins when they pay, a dull note when they don't.</summary>
        private void PlayVerdict(JudgeResult result)
        {
            if(result.Verdict == Verdict.DreamMeal) Play(sounds.dream, 0.9f, 0f);
            else if(result.IsPaid) Play(sounds.coins, 0.8f);
            else Play(sounds.noPay, 0.7f, 0f);
        }
    }
}
