using System;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class AudioClock
{
    private readonly Action<string> log;
    private AudioSource? audioSource;
    private double baseAudioSeconds;
    private float baseUnitySeconds;
    private float lastAudioUnavailableLogTime = -10.0f;

    public AudioClock(Action<string> log)
    {
        this.log = log;
    }

    public bool TryStart(bool useAudioTime, out double audioSeconds)
    {
        audioSeconds = 0.0;

        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            log("scrController.instance was not found.");
            return false;
        }

        audioSource = ReflectionCache.FindAudioSource(controller);
        if (audioSource == null || audioSource.clip == null || !audioSource.isPlaying)
        {
            LogAudioUnavailable(audioSource);
            return false;
        }

        baseAudioSeconds = ReadAudioSeconds();
        baseUnitySeconds = Time.unscaledTime;
        audioSeconds = baseAudioSeconds;

        if (!useAudioTime)
        {
            log("UseAudioTime is OFF. Using Unity unscaled time anchored to current audio time.");
        }

        return true;
    }

    private void LogAudioUnavailable(AudioSource? source)
    {
        if (Time.unscaledTime - lastAudioUnavailableLogTime < 1.0f)
        {
            return;
        }

        lastAudioUnavailableLogTime = Time.unscaledTime;

        if (source == null || source.clip == null)
        {
            log("AudioSource with a loaded clip was not found.");
            return;
        }

        log("AudioSource was found but is not playing.");
    }

    public bool TryGetSeconds(bool useAudioTime, out double seconds)
    {
        seconds = 0.0;
        if (audioSource == null || audioSource.clip == null)
        {
            return false;
        }

        seconds = useAudioTime
            ? ReadAudioSeconds()
            : baseAudioSeconds + (Time.unscaledTime - baseUnitySeconds);

        return true;
    }

    private double ReadAudioSeconds()
    {
        if (audioSource == null || audioSource.clip == null || audioSource.clip.frequency <= 0)
        {
            return 0.0;
        }

        return audioSource.timeSamples / (double)audioSource.clip.frequency;
    }
}
