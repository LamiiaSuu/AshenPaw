using System;
using UnityEngine;
using UnityEngine.Audio;

namespace AshenPaw
{
    public enum AudioCategory
    {
        Ambience,
        Bgm,
        Sfx,
        Voices
    }

    public static class GameAudioSettings
    {
        private static AudioMixer mixer;

        private static AudioMixer Mixer
        {
            get
            {
                if (mixer == null)
                    mixer = Resources.Load<AudioMixer>("AshenPawAudio");

                return mixer;
            }
        }

        public static AudioMixerGroup GetGroup(AudioCategory category)
        {
            if (Mixer == null) return null;

            var groups = Mixer.FindMatchingGroups("Master/" + CategoryName(category));
            return groups.Length > 0 ? groups[0] : null;
        }

        public static float GetVolume(AudioCategory category)
        {
            return Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey(category), 0.5f));
        }

        public static void SetVolume(AudioCategory category, float volume)
        {
            volume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(VolumeKey(category), volume);
            ApplyVolume(category, volume);
        }

        public static void ApplySavedVolumes()
        {
            foreach (AudioCategory category in Enum.GetValues(typeof(AudioCategory)))
                ApplyVolume(category, GetVolume(category));
        }

        public static void Save()
        {
            PlayerPrefs.Save();
        }

        private static void ApplyVolume(AudioCategory category, float volume)
        {
            if (Mixer == null) return;

            float gain = volume * 2f;
            float decibels = gain > 0f ? Mathf.Max(-80f, 20f * Mathf.Log10(gain)) : -80f;
            Mixer.SetFloat(CategoryName(category) + "Volume", decibels);
        }

        private static string VolumeKey(AudioCategory category) => "Audio." + CategoryName(category);

        private static string CategoryName(AudioCategory category)
        {
            return category switch
            {
                AudioCategory.Bgm => "BGM",
                AudioCategory.Sfx => "SFX",
                _ => category.ToString()
            };
        }
    }
}
