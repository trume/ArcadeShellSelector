using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ArcadeShellSelector
{
    /// <summary>
    /// Captures system audio via WASAPI loopback and produces 6 frequency band levels
    /// using a simple FFT. No interference with the LibVLC playback pipeline.
    /// </summary>
    internal sealed class SpectrumAnalyzer : IDisposable
    {
        public const int BandCount = 6;

        // FFT size — 1024 samples gives ~43 Hz per bin at 44100 Hz
        private const int FftSize = 1024;

        private WasapiLoopbackCapture? _capture;
        private readonly float[] _buffer = new float[FftSize];
        private int _bufferPos;
        private readonly object _lock = new();
        private bool _disposed;

        // Latest band levels (0.0 – 1.0), updated from capture thread
        private readonly float[] _bands = new float[BandCount];

        // Smoothed band levels for display
        private readonly float[] _smoothBands = new float[BandCount];

        /// <summary>Get a snapshot of the current 6 band levels (0.0–1.0).</summary>
        public void GetBands(float[] dest)
        {
            lock (_lock)
            {
                for (int i = 0; i < BandCount && i < dest.Length; i++)
                    dest[i] = _smoothBands[i];
            }
        }

        public void Start()
        {
            if (_disposed) return;
            try
            {
                // Use shortest possible capture buffer for minimal latency
                _capture = new WasapiLoopbackCapture()
                {
                    ShareMode = AudioClientShareMode.Shared
                };
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += (_, __) => { };
                _capture.StartRecording();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("SPECTRUM", "Loopback capture failed: " + ex.Message);
            }
        }

        public void Stop()
        {
            try { _capture?.StopRecording(); } catch { }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            var wf = _capture?.WaveFormat;
            if (wf == null || e.BytesRecorded == 0) return;

            int channels = wf.Channels;
            int bytesPerSample = wf.BitsPerSample / 8;
            bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat;

            int sampleCount = e.BytesRecorded / (bytesPerSample * channels);

            // When we get a large chunk, skip ahead to the most recent samples
            // to minimize latency. Only keep the last FftSize samples.
            int startSample = Math.Max(0, sampleCount - FftSize);

            for (int i = startSample; i < sampleCount; i++)
            {
                int offset = i * bytesPerSample * channels;
                float sample;

                if (isFloat && bytesPerSample == 4)
                    sample = BitConverter.ToSingle(e.Buffer, offset);
                else if (bytesPerSample == 2)
                    sample = BitConverter.ToInt16(e.Buffer, offset) / 32768f;
                else
                    continue;

                // Mix to mono (take first channel only for speed)
                _buffer[_bufferPos++] = sample;

                if (_bufferPos >= FftSize)
                {
                    _bufferPos = 0;
                    ProcessFft();
                }
            }
        }

        private void ProcessFft()
        {
            // Copy buffer and apply Hann window
            var real = new float[FftSize];
            var imag = new float[FftSize];
            for (int i = 0; i < FftSize; i++)
            {
                float window = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
                real[i] = _buffer[i] * window;
            }

            // In-place FFT (Cooley-Tukey radix-2 DIT)
            Fft(real, imag);

            // Compute magnitudes for the first half of the spectrum
            int halfN = FftSize / 2;
            var mag = new float[halfN];
            for (int i = 0; i < halfN; i++)
                mag[i] = MathF.Sqrt(real[i] * real[i] + imag[i] * imag[i]);

            // WASAPI loopback is typically 48000 Hz but could vary
            float sampleRate = _capture?.WaveFormat?.SampleRate ?? 48000f;
            float binHz = sampleRate / FftSize;

            // Band frequency ranges (Hz) — logarithmic spacing:
            //   0: 20-80,  1: 80-250,  2: 250-800,
            //   3: 800-2500,  4: 2500-8000,  5: 8000-20000
            int[] bandEdges = { 20, 80, 250, 800, 2500, 8000, 20000 };

            var newBands = new float[BandCount];
            for (int b = 0; b < BandCount; b++)
            {
                int binStart = Math.Max(1, (int)(bandEdges[b] / binHz));
                int binEnd = Math.Min(halfN - 1, (int)(bandEdges[b + 1] / binHz));
                if (binEnd <= binStart) binEnd = binStart + 1;

                float sum = 0f;
                for (int i = binStart; i <= binEnd; i++)
                    sum += mag[i];
                newBands[b] = sum / (binEnd - binStart + 1);
            }

            // Normalize: scale so typical music fills 0–1 range.
            // Apply log scaling for perceptual loudness.
            // Per-band reference levels: low bands have much more energy,
            // so they need a higher threshold to avoid saturation.
            float[] bandRefLevel = { 0.1f, 0.05f, 0.025f, 0.01f, 0.005f, 0.003f };
            const float dbRange = 50f;
            for (int b = 0; b < BandCount; b++)
            {
                float db = 20f * MathF.Log10(Math.Max(newBands[b], 1e-10f) / bandRefLevel[b]);
                newBands[b] = Math.Clamp(db / dbRange, 0f, 1f);
            }

            lock (_lock)
            {
                const float attack = 0.8f;  // very fast rise
                const float decay = 0.35f;  // faster fall for tighter tracking
                for (int b = 0; b < BandCount; b++)
                {
                    float target = newBands[b];
                    float current = _smoothBands[b];
                    _smoothBands[b] = target > current
                        ? current + (target - current) * attack
                        : current + (target - current) * decay;
                    _bands[b] = newBands[b];
                }
            }
        }

        /// <summary>Radix-2 Cooley-Tukey FFT in-place.</summary>
        private static void Fft(float[] real, float[] imag)
        {
            int n = real.Length;
            // Bit-reversal permutation
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imag[i], imag[j]) = (imag[j], imag[i]);
                }
            }

            // FFT butterfly
            for (int len = 2; len <= n; len <<= 1)
            {
                float angle = -2f * MathF.PI / len;
                float wRe = MathF.Cos(angle);
                float wIm = MathF.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    float curRe = 1f, curIm = 0f;
                    for (int j = 0; j < len / 2; j++)
                    {
                        int u = i + j;
                        int v = i + j + len / 2;
                        float tRe = curRe * real[v] - curIm * imag[v];
                        float tIm = curRe * imag[v] + curIm * real[v];
                        real[v] = real[u] - tRe;
                        imag[v] = imag[u] - tIm;
                        real[u] += tRe;
                        imag[u] += tIm;
                        float newCurRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = newCurRe;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _capture?.StopRecording(); } catch { }
            try { _capture?.Dispose(); } catch { }
        }
    }
}
