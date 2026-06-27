using System;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class AudioClock
{
    private readonly Action<string> log;
    private object? conductor;
    private AudioSource? audioSource;
    private double baseClockSeconds;
    private float baseUnitySeconds;
    private float lastAudioUnavailableLogTime = -10.0f;

    public ClockMode ActiveMode { get; private set; }

    public AudioClock(Action<string> log)
    {
        this.log = log;
    }

    public bool TryStart(ClockMode mode, out double clockSeconds)
    {
        clockSeconds = 0.0;
        ActiveMode = mode;
        conductor = ReflectionCache.GetSingletonInstance("scrConductor");
        audioSource = FindSongAudioSource();

        if (mode == ClockMode.ConductorSongPosition)
        {
            if (!TryReadConductorSongPosition(out clockSeconds))
            {
                log("waiting for clock: scrConductor song position was not available.");
                return false;
            }

            LogClockReady(mode, clockSeconds);
            return true;
        }

        if (mode == ClockMode.AudioSourceTimeSamples)
        {
            if (audioSource == null || audioSource.clip == null || !audioSource.isPlaying)
            {
                LogAudioUnavailable(audioSource);
                log("waiting for clock: AudioSourceTimeSamples is not ready.");
                return false;
            }

            clockSeconds = ReadAudioSeconds();
            LogClockReady(mode, clockSeconds);
            return true;
        }

        baseClockSeconds = ReadBestAvailableSongSeconds();
        baseUnitySeconds = Time.unscaledTime;
        clockSeconds = baseClockSeconds;
        LogClockReady(mode, clockSeconds);
        return true;
    }

    private AudioSource? FindSongAudioSource()
    {
        if (conductor != null)
        {
            object? song = ReflectionCache.ReadMember(conductor, "song");
            if (song is AudioSource source)
            {
                return source;
            }
        }

        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            return null;
        }

        return ReflectionCache.FindAudioSource(controller);
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

    public bool TryGetSeconds(ClockMode mode, out double seconds)
    {
        seconds = 0.0;

        if (mode == ClockMode.ConductorSongPosition)
        {
            if (!TryReadConductorSongPosition(out seconds))
            {
                log("waiting for clock: scrConductor song position was not available.");
                return false;
            }

            return true;
        }

        if (mode == ClockMode.AudioSourceTimeSamples)
        {
            if (audioSource == null || audioSource.clip == null)
            {
                return false;
            }

            seconds = ReadAudioSeconds();
            return true;
        }

        seconds = baseClockSeconds + (Time.unscaledTime - baseUnitySeconds);
        return true;
    }

    private void LogClockReady(ClockMode mode, double clockSeconds)
    {
        log($"Clock ready: type={mode} time={clockSeconds:F6}s");
    }

    private bool TryReadConductorSongPosition(out double seconds)
    {
        seconds = 0.0;
        return conductor != null &&
               ReflectionCache.TryReadDouble(
                   conductor,
                   out seconds,
                   "songposition_minusi",
                   "songposition",
                   "songPosition");
    }

    private double ReadBestAvailableSongSeconds()
    {
        if (TryReadConductorSongPosition(out double conductorSeconds))
        {
            return conductorSeconds;
        }

        if (audioSource != null && audioSource.clip != null)
        {
            return ReadAudioSeconds();
        }

        return 0.0;
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
