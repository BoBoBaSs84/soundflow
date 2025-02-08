using System;
using System.Buffers;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;

namespace SoundFlow.Components;

/// <summary>
/// A sound player that plays audio from a data provider.
/// </summary>
public sealed class SoundPlayer(ISoundDataProvider dataProvider) : SoundComponent, ISoundPlayer
{
    private readonly ISoundDataProvider _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
    private int _samplePosition;
    private float _currentFrame;
    private float _playbackSpeed = 1.0f;

    /// <summary>
    /// Playback speed
    /// </summary>
    /// <value>Playback speed must be greater than zero.</value>
    /// <exception cref="ArgumentOutOfRangeException">Playback speed must be greater than zero.</exception>
    public float PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Playback speed must be greater than zero.");
            _playbackSpeed = value;
        }
    }

    /// <inheritdoc />
    public override string Name { get; set; } = "Player";

    /// <inheritdoc />
    public PlaybackState State { get; private set; }

    /// <inheritdoc />
    public bool IsLooping { get; set; }

    /// <inheritdoc />
    public float Time => (float)_samplePosition / AudioEngine.Channels / AudioEngine.Instance.SampleRate / PlaybackSpeed;

    /// <inheritdoc />
    public float Duration => (float)_dataProvider.Length / AudioEngine.Channels / AudioEngine.Instance.SampleRate / PlaybackSpeed;

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> output)
    {
        if (State != PlaybackState.Playing)
            return;

        var channels = AudioEngine.Channels;
        var speed = PlaybackSpeed;
        var outputSampleCount = output.Length;
        var outputFrameCount = outputSampleCount / channels;

        // Calculate the number of source frames required
        var requiredSourceFrames = (int)Math.Ceiling(outputFrameCount * speed) + 2;
        var requiredSourceSamples = requiredSourceFrames * channels;

        var sourceSamples = ArrayPool<float>.Shared.Rent(requiredSourceSamples);
        var sourceSpan = sourceSamples.AsSpan(0, requiredSourceSamples);
        var sourceSamplesRead = _dataProvider.ReadBytes(sourceSpan);

        if (sourceSamplesRead == 0)
        {
            ArrayPool<float>.Shared.Return(sourceSamples);
            HandleEndOfStream(output);
            return;
        }

        var sourceFramesRead = sourceSamplesRead / channels;
        var outputFrameIndex = 0;

        // Process output frames with linear interpolation
        while (outputFrameIndex < outputFrameCount && _currentFrame < sourceFramesRead - 1)
        {
            var sourceFrame = _currentFrame;
            var frameIndex0 = (int)sourceFrame;
            var t = sourceFrame - frameIndex0;

            for (var ch = 0; ch < channels; ch++)
            {
                var sampleIndex0 = frameIndex0 * channels + ch;
                var sampleIndex1 = (frameIndex0 + 1) * channels + ch;

                if (sampleIndex1 >= sourceSamplesRead)
                    break;

                var sample0 = sourceSamples[sampleIndex0];
                var sample1 = sourceSamples[sampleIndex1];
                output[outputFrameIndex * channels + ch] = sample0 * (1 - t) + sample1 * t;
            }

            outputFrameIndex++;
            _currentFrame += speed;
        }

        // Clear any remaining output if underflow occurred.
        if (outputFrameIndex < outputFrameCount)
        {
            output.Slice(outputFrameIndex * channels, (outputFrameCount - outputFrameIndex) * channels).Clear();
        }

        // Update playback position.
        var framesConsumed = (int)_currentFrame;
        _samplePosition += framesConsumed * channels;
        _currentFrame -= framesConsumed;

        ArrayPool<float>.Shared.Return(sourceSamples);

        if (framesConsumed >= sourceFramesRead - 1) 
            HandleEndOfStream(output[(outputFrameIndex * channels)..]);
    }

    /// <summary>
    /// Handles the end-of-stream condition.
    /// </summary>
    private void HandleEndOfStream(Span<float> buffer)
    {
        if (IsLooping)
        {
            Seek(0);
            _currentFrame = 0f;
            GenerateAudio(buffer); // Process the buffer again after seeking.
        }
        else
        {
            State = PlaybackState.Stopped;
            OnPlaybackEnded();
            buffer.Clear();
        }
    }

    /// <summary>
    /// Invokes the PlaybackEnded event.
    /// </summary>
    private void OnPlaybackEnded()
    {
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
        if (!IsLooping)
        {
            Enabled = false;
            State = PlaybackState.Stopped;
        }
    }

    /// <summary>
    /// Occurs when playback ends.
    /// </summary>
    public event EventHandler<EventArgs>? PlaybackEnded;

    #region Audio Playback Control

    /// <inheritdoc />
    public void Play()
    {
        Enabled = true;
        State = PlaybackState.Playing;
    }

    /// <inheritdoc />
    public void Pause()
    {
        Enabled = false;
        State = PlaybackState.Paused;
    }

    /// <inheritdoc />
    public void Stop()
    {
        Pause();
        Seek(0);
    }

    /// <inheritdoc />
    public void Seek(float time)
    {
        var sampleOffset = (int)(time / Duration * _dataProvider.Length);
        Seek(sampleOffset);
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        if (!_dataProvider.CanSeek)
            throw new InvalidOperationException("Seeking is not supported for this sound.");

        _dataProvider.Seek(sampleOffset);
        _samplePosition = sampleOffset;
        
        // Reset the fractional frame index for interpolation relative to the new stream position.
        _currentFrame = 0f;
    }

    #endregion
}