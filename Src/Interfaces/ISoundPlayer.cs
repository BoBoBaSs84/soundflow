﻿using SoundFlow.Enums;

namespace SoundFlow.Interfaces;

/// <summary>
/// Defines the interface for a sound player component.
/// </summary>
public interface ISoundPlayer
{
    /// <summary>
    /// Gets the current playback state of the sound player.
    /// </summary>
    PlaybackState State { get; }

    /// <summary>
    /// Gets a value indicating whether the sound player is currently looping the audio.
    /// </summary>
    bool IsLooping { get; }

    /// <summary>
    /// Gets or sets the playback speed of the sound player.
    /// A value of 1.0 represents normal speed. Values greater than 1.0 increase the speed, and values less than 1.0 decrease it.
    /// </summary>
    /// <remarks>The current implementation uses linear interpolation which may affect the pitch.</remarks>
    float PlaybackSpeed { get; set; }

    /// <summary>
    /// Gets the current playback time in seconds, relative to the beginning of the audio.
    /// </summary>
    float Time { get; }

    /// <summary>
    /// Gets the total duration of the audio in seconds.
    /// </summary>
    float Duration { get; }

    /// <summary>
    /// Starts or resumes playback of the audio from the current position.
    /// If the player is already playing, calling this method may have no effect.
    /// If the player is stopped, playback starts from the beginning.
    /// If the player is paused, playback resumes from the paused position.
    /// </summary>
    void Play();

    /// <summary>
    /// Pauses playback of the audio at the current position.
    /// If the player is already paused or stopped, calling this method may have no effect.
    /// Playback can be resumed from the paused position by calling <see cref="Play"/>.
    /// </summary>
    void Pause();

    /// <summary>
    /// Stops playback of the audio and resets the playback position to the beginning.
    /// If the player is already stopped, calling this method has no effect.
    /// After stopping, playback can be restarted from the beginning by calling <see cref="Play"/>.
    /// </summary>
    void Stop();

    /// <summary>
    /// Seeks to a specific time in the audio playback.
    /// </summary>
    /// <param name="time">The time in seconds to seek to, relative to the beginning of the audio.</param>
    void Seek(float time);

    /// <summary>
    /// Seeks to a specific sample offset in the audio playback.
    /// </summary>
    /// <param name="sampleOffset">The sample offset to seek to, relative to the beginning of the audio data.</param>
    void Seek(int sampleOffset);
}