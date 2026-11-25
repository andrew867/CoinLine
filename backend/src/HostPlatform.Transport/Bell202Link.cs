namespace HostPlatform.Transport;

/// <summary>
/// Bell 202 audio at 1200 baud: mark 1200 Hz, space 2200 Hz, 8 data bits,
/// least-significant bit first, one start bit, one stop bit. This is the
/// tone pair the minimodem "1200" mode uses. Samples are 8000 Hz, which is
/// the G.711 rate, so a SIP call can feed this modem without resampling.
/// </summary>
public sealed class Bell202Link
{
    public const int SampleRate = 8000;
    public const double Baud = 1200;
    public const double SamplesPerBit = SampleRate / Baud;
    public const double MarkHz = 1200;
    public const double SpaceHz = 2200;
    public const int LeadingMarkBits = 48;

    private const int Window = 8;
    private const float Amplitude = 0.45f;

    private readonly Queue<bool> _txBits = new();
    private double _txPhase;
    private double _bitSamplesLeft;
    private bool _currentMark = true;
    private bool _txPrimed;

    private readonly float[] _window = new float[Window];
    private int _windowCount;
    private int _windowPos;
    private bool _prevMark = true;
    private bool _rxIdle = true;
    private int _rxBit;
    private int _rxValue;
    private double _samplesToSample;

    public void Queue(ReadOnlySpan<byte> data)
    {
        if (!_txPrimed)
        {
            for (var i = 0; i < LeadingMarkBits; i++)
                _txBits.Enqueue(true);
            _txPrimed = true;
        }

        foreach (var value in data)
        {
            _txBits.Enqueue(false);
            for (var bit = 0; bit < 8; bit++)
                _txBits.Enqueue(((value >> bit) & 1) == 1);
            _txBits.Enqueue(true);
        }
    }

    /// <summary>Renders exactly <paramref name="sampleCount"/> samples. Idle time is mark tone.</summary>
    public float[] Render(int sampleCount)
    {
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            if (_bitSamplesLeft <= 0)
            {
                if (!_txBits.TryDequeue(out _currentMark))
                    _currentMark = true;
                _bitSamplesLeft += SamplesPerBit;
            }

            var step = 2 * Math.PI * (_currentMark ? MarkHz : SpaceHz) / SampleRate;
            _txPhase += step;
            if (_txPhase > Math.PI * 2)
                _txPhase -= Math.PI * 2;
            samples[i] = (float)(Math.Sin(_txPhase) * Amplitude);
            _bitSamplesLeft -= 1;
        }

        return samples;
    }

    public byte[] Push(ReadOnlySpan<float> samples)
    {
        var output = new List<byte>();
        foreach (var sample in samples)
        {
            _window[_windowPos] = sample;
            _windowPos = (_windowPos + 1) % Window;
            if (_windowCount < Window)
                _windowCount++;
            var (mark, energy) = Decide();
            if (energy < 0.02f)
                mark = true;

            if (_rxIdle)
            {
                if (_prevMark && !mark)
                {
                    _rxIdle = false;
                    _rxBit = 0;
                    _rxValue = 0;
                    _samplesToSample = SamplesPerBit * 1.5;
                }
            }
            else
            {
                _samplesToSample -= 1;
                if (_samplesToSample <= 0)
                {
                    if (_rxBit < 8)
                    {
                        if (mark)
                            _rxValue |= 1 << _rxBit;
                        _rxBit++;
                        _samplesToSample += SamplesPerBit;
                    }
                    else
                    {
                        if (mark)
                            output.Add((byte)_rxValue);
                        _rxIdle = true;
                        _samplesToSample = 0;
                    }
                }
            }

            _prevMark = mark;
        }

        return output.ToArray();
    }

    private (bool Mark, float Energy) Decide()
    {
        if (_windowCount < Window)
            return (true, 0);
        double markI = 0, markQ = 0, spaceI = 0, spaceQ = 0;
        for (var i = 0; i < Window; i++)
        {
            var sample = _window[(_windowPos + i) % Window];
            var t = i / (double)SampleRate;
            var markAngle = 2 * Math.PI * MarkHz * t;
            var spaceAngle = 2 * Math.PI * SpaceHz * t;
            markI += sample * Math.Cos(markAngle);
            markQ += sample * Math.Sin(markAngle);
            spaceI += sample * Math.Cos(spaceAngle);
            spaceQ += sample * Math.Sin(spaceAngle);
        }

        var markEnergy = markI * markI + markQ * markQ;
        var spaceEnergy = spaceI * spaceI + spaceQ * spaceQ;
        return (markEnergy >= spaceEnergy, (float)Math.Max(markEnergy, spaceEnergy));
    }
}
