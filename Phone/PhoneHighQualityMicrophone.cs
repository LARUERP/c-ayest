using System;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LabFusion.Utilities;
using LabFusion.Voice;
using LabFusion.Voice.Unity;
using UnityEngine;

namespace FusionDarkRP.Telephone;

public sealed class PhoneHighQualityMicrophone
{
    private static readonly FieldInfo clipField =
        typeof(UnityVoiceReceiver).GetField(
            "_voiceClip",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    private AudioClip clip;

    private int cursor;

    private string session = "";

    private float last;

    private float warning;

    private PhoneStreamEncoder encoder = new PhoneStreamEncoder();

    public void Tick(
        string id,
        Action<byte[]> send,
        Action<string> warn,
        Func<short[], int, short[]> mix)
    {
        if (id != session)
        {
            session = id;
            clip = null;
            encoder = new PhoneStreamEncoder();
            last = Time.realtimeSinceStartup;
        }

        if (id == "")
        {
            return;
        }

        try
        {
            IVoiceManager voiceManager = VoiceInfo.VoiceManager;

            IVoiceReceiver receiver =
                voiceManager != null ? voiceManager.GetReceiver() : null;

            AudioClip microphoneClip = null;

            if (receiver is UnityVoiceReceiver &&
                clipField != null)
            {
                microphoneClip =
                    clipField.GetValue(receiver) as AudioClip;
            }

            if ((Object)(object)microphoneClip == (Object)null)
            {
                if (Time.realtimeSinceStartup - last > 2f)
                {
                    throw new InvalidOperationException(
                        "Micro Fusion indisponible pour l’audio HD."
                    );
                }

                return;
            }

            string microphoneName =
                UnityVoiceReceiver.GetValidMicrophoneName();

            if (!Microphone.IsRecording(microphoneName))
            {
                return;
            }

            int position = Microphone.GetPosition(microphoneName);

            if (position < 0 || position >= microphoneClip.samples)
            {
                return;
            }

            if ((Object)(object)clip != (Object)(object)microphoneClip)
            {
                clip = microphoneClip;
                cursor = position;
                last = Time.realtimeSinceStartup;
                encoder = new PhoneStreamEncoder();
                return;
            }

            int available = PhoneMicrophoneRules.Available(
                cursor,
                position,
                microphoneClip.samples
            );

            if (available == 0)
            {
                return;
            }

            float currentTime = Time.realtimeSinceStartup;

            if (currentTime - last > 0.2f)
            {
                cursor = position;
                last = currentTime;
                encoder = new PhoneStreamEncoder();
                return;
            }

            Il2CppStructArray<float> samples =
                new Il2CppStructArray<float>(
                    (long)(available * microphoneClip.channels)
                );

            if (!microphoneClip.GetData(samples, cursor))
            {
                throw new InvalidOperationException(
                    "Lecture micro HD impossible."
                );
            }

            float[] audioData =
                new float[((Il2CppArrayBase<float>)(object)samples).Length];

            InteropUtilities.Copy(
                ((Il2CppObjectBase)samples).Pointer,
                IntPtr.Size,
                ((Il2CppArrayBase<float>)(object)samples).Length,
                audioData
            );

            cursor = position;
            last = currentTime;

            short[] monoSamples =
                PhoneMicrophoneRules.Mono(
                    audioData,
                    microphoneClip.channels
                );

            encoder.Push(
                mix(monoSamples, microphoneClip.frequency),
                microphoneClip.frequency,
                send
            );
        }
        catch (Exception ex)
        {
            if (Time.realtimeSinceStartup >= warning)
            {
                warning = Time.realtimeSinceStartup + 5f;
                warn(ex.Message);
            }

            clip = null;
            encoder = new PhoneStreamEncoder();
        }
    }
}
