using System.Buffers;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Exceptions;
using SoundFlow.Interfaces;
using SoundFlow.Utils;

namespace SoundFlow.Abstracts;

/// <summary>
///     This event is raised when samples are processed by Input or Output components.
/// </summary>
public delegate void AudioProcessCallback(Span<float> samples, Capability capability);

/// <summary>
///     The base class for audio engines.
///     This class provides common functionality for audio engines such as sample rate and
///     channel count management, thread management, and solo/mute functionality.
/// </summary>
public abstract class AudioEngine : IDisposable
{
    private static AudioEngine? _instance;

    private bool _soloedComponentExists;
    private SoundComponent? _soloedComponent;

    private readonly object _lock = new();

    private Thread? _audioThread;
    private readonly ManualResetEvent _audioThreadStarted = new(false);
    private readonly ManualResetEvent _stopAudioThread = new(false);

    /// <summary>
    ///     Constructs a new instance of <see cref="AudioEngine" />.
    /// </summary>
    /// <param name="sampleRate">The sampling rate used by the engine.</param>
    /// <param name="capability">The audio device capability.</param>
    /// <param name="sampleFormat">The sample format used by the engine.</param>
    /// <param name="channels">The number of audio channels.</param>
    protected AudioEngine(int sampleRate, Capability capability, SampleFormat sampleFormat, int channels)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        if (_instance != null)
            throw new InvalidOperationException("An instance of AudioEngine is already initialized.");

        _instance = this;
        SampleRate = sampleRate;
        InverseSampleRate = 1f / sampleRate;
        Channels = channels;
        Capability = capability;
        SampleFormat = sampleFormat;

        SetupAudioThread();
    }

    private void SetupAudioThread()
    {
        // Start audio device in a dedicated thread
        if (RequiresBackendThread)
        {
            _audioThread = new Thread(AudioThreadLoop);
            _audioThread.Start();
            _audioThreadStarted.WaitOne();
        }
        else
        {
            InitializeAudioDevice();
        }
    }
    
    private void AudioThreadLoop()
    {
        // Backend-specific initialization that requires a separate thread
        InitializeAudioDevice();

        _audioThreadStarted.Set();

        while (!_stopAudioThread.WaitOne(0))
        {
            // Process audio data in a loop
            ProcessAudioData();
        }

        // Backend-specific cleanup
        CleanupAudioDevice();
    }

    /// <summary>
    ///     Initializes the audio device and starts the audio processing loop.
    /// </summary>
    protected abstract void InitializeAudioDevice(); // Backend-specific initialization
    
    /// <summary>
    ///     Processes audio data in a loop.
    /// </summary>
    protected abstract void ProcessAudioData(); // Backend-specific audio processing loop
    
    /// <summary>
    ///     Cleans up the audio device.
    /// </summary>
    protected abstract void CleanupAudioDevice(); // Backend-specific cleanup

    /// <summary>
    ///     Gets the configured sample format.
    /// </summary>
    public SampleFormat SampleFormat { get; }

    /// <summary>
    ///     Gets the configured sample rate (samples per second).
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    ///     Gets the inverse of the configured sample rate (seconds per sample).
    /// </summary>
    public float InverseSampleRate { get; }

    /// <summary>
    ///     Gets the number of configured audio channels.
    /// </summary>
    public static int Channels { get; private set; } = 2; // Stereo by default

    /// <summary>
    ///     Gets or sets the capability of the audio engine.
    /// </summary>
    public Capability Capability { get; }
    
    /// <summary>
    ///     Gets whether the backend requires a dedicated thread for audio processing.
    ///     True if <see cref="AudioEngine"/> manages a backend thread, false otherwise.
    ///     Subclasses override to indicate backend threading requirements.
    /// </summary>
    protected virtual bool RequiresBackendThread { get; } = true;
    
    /// <summary>
    ///     Gets the audio engine instance.
    /// </summary>
    public static AudioEngine Instance => _instance ?? throw new BackendException("None", Result.NoBackend,
        "AudioEngine is not initialized yet. Create an instance of your backend first.");

    /// <summary>
    ///     Cleans up resources before the object is garbage collected.
    /// </summary>
    ~AudioEngine()
    {
        Dispose(false);
    }

    /// <summary>
    ///     Solos the specified sound component, muting all other components.
    /// </summary>
    /// <param name="component">The component to solo.</param>
    public void SoloComponent(SoundComponent component)
    {
        lock (_lock)
        {
            _soloedComponentExists = true;
            _soloedComponent = component;
        }
    }

    /// <summary>
    ///     Unsolos the specified sound component.
    /// </summary>
    /// <param name="component">The component to unsolo.</param>
    public void UnsoloComponent(SoundComponent component)
    {
        lock (_lock)
        {
            if (_soloedComponent == component)
            {
                _soloedComponentExists = false;
                _soloedComponent = null;
            }
        }
    }

    /// <summary>
    ///     Processes the audio graph synchronously.
    /// </summary>
    /// <param name="output">A pointer to the output buffer.</param>
    /// <param name="length">The length of the output buffer in samples.</param>
    protected void ProcessGraph(nint output, int length)
    {
        if (length <= 0 || output == nint.Zero)
            return;

        // Use a local variable to store the processed float samples
        float[]? tempBuffer = null;
        Span<float> buffer;

        if (SampleFormat == SampleFormat.F32)
            buffer = Extensions.GetSpan<float>(output, length);
        else
        {
            tempBuffer = ArrayPool<float>.Shared.Rent(length);
            buffer = tempBuffer.AsSpan(0, length);
        }


        // Process soloed component or the entire graph
        lock (_lock)
        {
            if (_soloedComponentExists)
                _soloedComponent?.Process(buffer);
            else
                Mixer.Master.Process(buffer);
        }

        // Handle output based on sample format
        switch (SampleFormat)
        {
            case SampleFormat.S16:
                ConvertAndCopyToOutput<short>(output, length, buffer, short.MaxValue);
                break;
            case SampleFormat.S24: // For Signed 24-bit PCM, using int for conversion
                ConvertAndCopyToOutputS24(output, length, buffer);
                break;
            case SampleFormat.S32:
                ConvertAndCopyToOutput<int>(output, length, buffer, int.MaxValue);
                break;
            case SampleFormat.U8: // Unsigned 8-bit PCM
                ConvertAndCopyToOutput<byte>(output, length, buffer, 128, true);
                break;
            case SampleFormat.F32:
                break;
            default:
                throw new NotSupportedException($"Sample format {SampleFormat} is not supported.");
        }

        OnAudioProcessed?.Invoke(buffer, Capability.Playback);

        if (tempBuffer != null)
            ArrayPool<float>.Shared.Return(tempBuffer);
    }

    private static void ConvertAndCopyToOutputS24(nint output, int length, Span<float> floatBuffer)
    {
        // Get a span of bytes representing the output buffer
        var outputSpan = Extensions.GetSpan<byte>(output, length * 3); // 3 bytes per S24 sample

        for (int i = 0, j = 0; i < length; i++, j += 3)
        {
            var clipped = Math.Clamp(floatBuffer[i], -1f, 1f);
            var sample24 = (int)(clipped * 8388607); // 2^23 - 1  (maximum positive value for 24-bit signed)

            // Manually pack the 24-bit sample into 3 bytes (Little Endian)
            outputSpan[j] = (byte)(sample24 & 0xFF);
            outputSpan[j + 1] = (byte)((sample24 >> 8) & 0xFF);
            outputSpan[j + 2] = (byte)((sample24 >> 16) & 0xFF);

            floatBuffer[i] = 0;
        }
    }


    private void ConvertAndCopyToOutput<T>(nint output, int length, Span<float> floatBuffer, float maxValue,
        bool isUnsigned = false) where T : unmanaged
    {
        var outputSpan = Extensions.GetSpan<T>(output, length);
        for (var i = 0; i < length; i++)
        {
            var clipped = Math.Clamp(floatBuffer[i], -1f, 1f);
            var scaledValue = clipped * maxValue;

            if (isUnsigned)
                scaledValue += maxValue;

            if (typeof(T) == typeof(int) && SampleFormat == SampleFormat.S24) // Special S24 handling
            {
                var intValue = (int)scaledValue;
                var intBytes = BitConverter.GetBytes(intValue); // Get the bytes of the integer
                outputSpan[i] =
                    (T)Convert.ChangeType(BitConverter.ToInt32(intBytes, 0),
                        typeof(T)); // Convert back, effectively using 32 bits for alignment
            }
            else
            {
                if (typeof(T) == typeof(int))
                    scaledValue = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, scaledValue));

                outputSpan[i] = (T)Convert.ChangeType(scaledValue, typeof(T));
            }

            floatBuffer[i] = 0;
        }
    }


    /// <summary>
    ///     Called by an implementation when the audio input has captured samples.
    /// </summary>
    /// <param name="input">A pointer to the input buffer.</param>
    /// <param name="length">The length of the input buffer in samples.</param>
    protected void ProcessAudioInput(nint input, int length)
    {
        if (length <= 0 || input == nint.Zero)
            return;

        var inputBuffer = Extensions.GetSpan<float>(input, length);
        OnAudioProcessed?.Invoke(inputBuffer, Capability.Record);
    }

    /// <summary>
    ///     Constructs a sound encoder specific to the implementation.
    /// </summary>
    /// <param name="filePath">The path to the file to write encoded audio to.</param>
    /// <param name="encodingFormat">The desired audio encoding format.</param>
    /// <param name="sampleFormat">The format of the input audio samples.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate of the input audio.</param>
    /// <returns>An instance of a sound encoder.</returns>
    protected internal abstract ISoundEncoder CreateEncoder(string filePath, EncodingFormat encodingFormat,
        SampleFormat sampleFormat, int channels, int sampleRate);

    /// <summary>
    ///     Constructs a sound decoder specific to the implementation.
    /// </summary>
    /// <param name="stream">The stream containing the audio data.</param>
    /// <returns>An instance of a sound decoder.</returns>
    protected internal abstract ISoundDecoder CreateDecoder(Stream stream);

    /// <summary>
    ///     Occurs when samples are processed by Input or Output components.
    /// </summary>
    public static event AudioProcessCallback? OnAudioProcessed;

    #region IDisposable Support

    /// <summary>
    ///     Gets a value indicating whether the audio engine has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    ///     Disposes of managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed) 
            return;

        if (RequiresBackendThread)
        {
            _stopAudioThread.Set();
            _audioThread?.Join();
        }
        
        _instance = null;
        IsDisposed = true;
    }

    /// <summary>
    ///     Disposes of the audio engine.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}