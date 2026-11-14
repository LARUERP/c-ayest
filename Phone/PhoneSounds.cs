using System;
using System.IO;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace FusionDarkRP.Telephone;

public sealed class PhoneSounds : IDisposable
{
    public static readonly string[] CallNames =
    {
        "Classique",
        "Cristal",
        "Orbital",
        "Silencieux"
    };

    public static readonly string[] MessageNames =
    {
        "Note",
        "Duo",
        "Carillon",
        "Silencieux"
    };

    private readonly string file;

    private readonly AudioClip[] clips =
        (AudioClip[])(object)new AudioClip[8];

    private GameObject go;
    private AudioSource source;

    public PhonePreferences Preferences { get; private set; }
        = new PhonePreferences();

    public PhoneSounds(string file)
    {
        this.file = file;

        if (File.Exists(file))
        {
            try
            {
                Preferences =
                    JsonSerializer.Deserialize<PhonePreferences>(
                        File.ReadAllText(file))
                    ?? new PhonePreferences();

                Preferences.Validate();
            }
            catch (Exception)
            {
                Preferences = new PhonePreferences();
            }
        }
    }

    public void Select(bool call, int choice)
    {
        PhonePreferences preferences = Preferences.Copy();

        if (call)
        {
            preferences.CallTone = choice;
        }
        else
        {
            preferences.MessageTone = choice;
        }

        preferences.Validate();

        Directory.CreateDirectory(
            Path.GetDirectoryName(file));

        File.WriteAllText(
            file + ".tmp",
            JsonSerializer.Serialize(preferences));

        File.Move(
            file + ".tmp",
            file,
            overwrite: true);

        Preferences = preferences;

        Play(call, choice);
    }

    public void SetVolume(float value)
    {
        PhonePreferences preferences = Preferences.Copy();

        preferences.RingVolume =
            Math.Max(0f, Math.Min(1f, value));

        preferences.Validate();

        Directory.CreateDirectory(
            Path.GetDirectoryName(file));

        File.WriteAllText(
            file + ".tmp",
            JsonSerializer.Serialize(preferences));

        File.Move(
            file + ".tmp",
            file,
            overwrite: true);

        Preferences = preferences;

        Message();
    }

    public void SetFps(bool video, int fps)
    {
        PhonePreferences preferences = Preferences.Copy();

        if (video)
        {
            preferences.VideoFps = fps;
        }
        else
        {
            preferences.PhotoFps = fps;
        }

        preferences.Validate();

        Directory.CreateDirectory(
            Path.GetDirectoryName(file));

        File.WriteAllText(
            file + ".tmp",
            JsonSerializer.Serialize(preferences));

        File.Move(
            file + ".tmp",
            file,
            overwrite: true);

        Preferences = preferences;
    }

    public void SetTheme(string theme)
    {
        PhonePreferences preferences = Preferences.Copy();

        preferences.Theme = theme;
        preferences.Validate();

        Directory.CreateDirectory(
            Path.GetDirectoryName(file));

        File.WriteAllText(
            file + ".tmp",
            JsonSerializer.Serialize(preferences));

        File.Move(
            file + ".tmp",
            file,
            overwrite: true);

        Preferences = preferences;
    }

    public void SetLanguage(string language)
    {
        PhonePreferences preferences = Preferences.Copy();

        preferences.Language = language;
        preferences.Validate();

        Directory.CreateDirectory(
            Path.GetDirectoryName(file));

        File.WriteAllText(
            file + ".tmp",
            JsonSerializer.Serialize(preferences));

        File.Move(
            file + ".tmp",
            file,
            overwrite: true);

        Preferences = preferences;
    }

    public AudioClip Clip(bool call, int choice)
    {
        if (choice == 3)
        {
            return null;
        }

        int index = (call ? 0 : 4) + choice;

        if ((Object)(object)clips[index] != (Object)null)
        {
            return clips[index];
        }

        float duration = call ? 2f : 0.65f;

        float[] samples =
            new float[(int)(24000f * duration)];

        for (int i = 0; i < samples.Length; i++)
        {
            float time = (float)i / 24000f;

            float frequency = !call
                ? choice switch
                {
                    1 => 740,
                    0 => 880,
                    _ => 1040
                }
                : choice switch
                {
                    1 => 660,
                    0 => 440,
                    _ => 520
                };

            bool active = !call
                ? time < 0.45f
                : time < 0.3f ||
                  (time > 0.45f && time < 0.75f);

            if (choice == 1)
            {
                frequency +=
                    (int)(time * 8f) % 2 * 220;
            }

            if (choice == 2)
            {
                frequency +=
                    (int)(time * 10f) % 4 * 110;
            }

            float envelope =
                Math.Min(1f, time * 80f) *
                Math.Min(
                    1f,
                    Math.Max(0f, (duration - time) * 80f));

            samples[i] = active
                ? 0.65f * envelope *
                  (float)Math.Sin(
                      Math.PI * 2.0 *
                      frequency * time)
                : 0f;
        }

        clips[index] = AudioClip.Create(
            "RP_Tone_" + index,
            samples.Length,
            1,
            24000,
            false);

        clips[index].SetData(samples, 0);

        return clips[index];
    }

    public void Message()
    {
        Play(
            call: false,
            Preferences.MessageTone);
    }

    public void Play(bool call, int choice)
    {
        AudioClip clip = Clip(call, choice);

        if ((Object)(object)clip == (Object)null)
        {
            return;
        }

        if ((Object)(object)source == (Object)null)
        {
            go = new GameObject("RP_LocalPhoneSounds");

            source = go.AddComponent<AudioSource>();

            source.spatialBlend = 0f;
            source.playOnAwake = false;
        }

        source.PlayOneShot(
            clip,
            Preferences.RingVolume);
    }

    public void Dispose()
    {
        if ((Object)(object)go != (Object)null)
        {
            Object.Destroy((Object)(object)go);
        }

        source = null;
        go = null;

        foreach (AudioClip clip in clips)
        {
            if ((Object)(object)clip != (Object)null)
            {
                Object.Destroy((Object)(object)clip);
            }
        }

        Array.Clear(clips, 0, clips.Length);
    }
}
