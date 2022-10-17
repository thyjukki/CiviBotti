using System;
using Microsoft.CognitiveServices.Speech.Audio;

namespace CiviBotti;

public class PushAudioOutputCallback : PushAudioOutputStreamCallback
{
    private byte[] _audioData;
    private DateTime _dt;
    private bool _firstWrite = true;
    private double _latency;

    /// <summary>
    /// Constructor
    /// </summary>
    public PushAudioOutputCallback()
    {
        Reset();
    }

    /// <summary>
    /// A callback which is invoked when the synthesizer has a output audio chunk to write out
    /// </summary>
    /// <param name="dataBuffer">The output audio chunk sent by synthesizer</param>
    /// <returns>Tell synthesizer how many bytes are received</returns>
    public override uint Write(byte[] dataBuffer)
    {
        if (_firstWrite)
        {
            _firstWrite = false;
            _latency = (DateTime.Now - _dt).TotalMilliseconds;
        }

        int oldSize = _audioData.Length;
        Array.Resize(ref _audioData, oldSize + dataBuffer.Length);
        for (int i = 0; i < dataBuffer.Length; ++i)
        {
            _audioData[oldSize + i] = dataBuffer[i];
        }

        Console.WriteLine($"{dataBuffer.Length} bytes received.");

        return (uint)dataBuffer.Length;
    }

    /// <summary>
    /// A callback which is invoked when the synthesizer is about to close the stream
    /// </summary>
    public override void Close()
    {
        Console.WriteLine("Push audio output stream closed.");
    }

    /// <summary>
    /// Get the received audio data
    /// </summary>
    /// <returns>The received audio data in byte array</returns>
    public byte[] GetAudioData()
    {
        return _audioData;
    }

    /// <summary>
    /// reset stream
    /// </summary>
    public void Reset()
    {
        _audioData = new byte[0];
        _dt = DateTime.Now;
        _firstWrite = true;
    }


    /// <summary>
    /// get latecny
    /// </summary>
    /// <returns></returns>
    public double GetLatency()
    {
        return _latency;
    }
}