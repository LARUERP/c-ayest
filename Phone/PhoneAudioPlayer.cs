
using System;
using Il2CppInterop.Runtime;
using LabFusion.Audio;
using UnityEngine;

namespace FusionDarkRP.Telephone
{
    public sealed class PhoneAudioPlayer : IDisposable
    {
        private readonly PhonePcmBuffer buffer;
        private readonly PhoneVoiceFilter filter;
        private readonly AudioClip clip;
        private readonly PhoneVoiceContinuity continuity =
            new PhoneVoiceContinuity();

        private readonly AudioClip.PCMReaderCallback callback;

        private float lastVolume;
        private bool disposed;

        public GameObject gameObject { get; }

        public Transform transform => gameObject.transform;

        public AudioSource AudioSource { get; }

        public PhoneAudioPlayer()
        {
            int outputSampleRate = AudioInfo.OutputSampleRate;

            filter = new PhoneVoiceFilter(
                outputSampleRate,
                18000.0
            );

            buffer = new PhonePcmBuffer(outputSampleRate);

            gameObject = new GameObject(
                "RP_BufferedPhoneAudio"
            );

            AudioSource = gameObject.AddComponent<AudioSource>();

            AudioSource.playOnAwake = false;
            AudioSource.loop = true;
            AudioSource.dopplerLevel = 0f;
            AudioSource.bypassReverbZones = true;
            AudioSource.reverbZoneMix = 0f;

            callback =
                DelegateSupport.ConvertDelegate<AudioClip.PCMReaderCallback>(
                    new Action<float[]>(Fill)
                );

            clip = AudioClip.Create(
                "RP_PhoneStreamingVoice",
                outputSampleRate,
                1,
                outputSampleRate,
                false,
                true,
                callback,
                null
            );

            AudioSource.clip = clip;
            AudioSource.Play();
        }

        private void Fill(float[] data)
        {
            if (disposed || data == null)
                return;

            buffer.Read(data);
        }

        public void Push(short[] pcm, float volume)
        {
            if (disposed || pcm == null || pcm.Length == 0)
                return;

            lastVolume = volume;

            pcm = continuity.Prepare(pcm);

            short[] filtered = new short[pcm.Length];

            for (int i = 0; i < pcm.Length; i++)
            {
                float sample = (float)pcm[i] / 32768f;
                float processed = filter.Process(sample);

                filtered[i] = (short)Math.Clamp(
                    PhoneVoiceContinuity.Limit(processed) * 32767f,
                    -32767f,
                    32767f
                );
            }

            buffer.Push(filtered, volume);
        }

        public void Conceal(int frames)
        {
            if (disposed)
                return;

            short[] samples = continuity.Fill(
                Math.Clamp(frames, 0, 20) *
                AudioInfo.OutputSampleRate / 100
            );

            short[] filtered = new short[samples.Length];

            for (int i = 0; i < samples.Length; i++)
            {
                float sample = (float)samples[i] / 32768f;

                filtered[i] = (short)(
                    PhoneVoiceContinuity.Limit(
                        filter.Process(sample)
                    ) * 32767f
                );
            }

            buffer.Push(filtered, lastVolume);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            buffer.Dispose();

            if (AudioSource != null)
                AudioSource.Stop();

            if (gameObject != null)
                UnityEngine.Object.Destroy(gameObject);

            if (clip != null)
                UnityEngine.Object.Destroy(clip);
        }
    }
}
