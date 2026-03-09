using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArcadeShellSelector
{
    /// <summary>
    /// Reads metadata from .MOD and .XM tracker music files.
    /// </summary>
    internal sealed class TrackerMetadata
    {
        public string Title { get; private set; } = "";
        public string Format { get; private set; } = "";
        public string? Tracker { get; private set; }
        public int Channels { get; private set; }
        public int Patterns { get; private set; }
        public int Instruments { get; private set; }
        public int Bpm { get; private set; }
        public int Tempo { get; private set; }
        public List<string> SampleNames { get; private set; } = new();

        public static TrackerMetadata? TryRead(string filePath)
        {
            try
            {
                var ext = Path.GetExtension(filePath);
                if (string.Equals(ext, ".xm", StringComparison.OrdinalIgnoreCase))
                    return ReadXm(filePath);
                if (string.Equals(ext, ".mod", StringComparison.OrdinalIgnoreCase))
                    return ReadMod(filePath);
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static TrackerMetadata ReadXm(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[80];
            if (fs.Read(header, 0, 80) < 80) return new TrackerMetadata { Format = "XM" };

            var meta = new TrackerMetadata
            {
                Format = "XM",
                Title = ReadString(header, 17, 20),
                Tracker = ReadString(header, 38, 20),
                Channels = BitConverter.ToUInt16(header, 68),
                Patterns = BitConverter.ToUInt16(header, 70),
                Instruments = BitConverter.ToUInt16(header, 72),
                Tempo = BitConverter.ToUInt16(header, 74),
                Bpm = BitConverter.ToUInt16(header, 76),
            };

            // Read instrument names — each instrument header starts with a 4-byte size + 22-byte name
            // Skip patterns first: header size is at offset 60 (4 bytes, LE)
            try
            {
                int headerSize = BitConverter.ToInt32(header, 60);
                long pos = 60 + headerSize; // start of pattern data

                // Skip all patterns
                for (int p = 0; p < meta.Patterns && pos < fs.Length; p++)
                {
                    fs.Position = pos;
                    var patHead = new byte[9];
                    if (fs.Read(patHead, 0, 9) < 9) break;
                    int patHeaderLen = BitConverter.ToInt32(patHead, 0);
                    int packedSize = BitConverter.ToUInt16(patHead, 7);
                    pos += patHeaderLen + packedSize;
                }

                // Now read instrument names
                for (int i = 0; i < meta.Instruments && pos < fs.Length; i++)
                {
                    fs.Position = pos;
                    var instHead = new byte[29]; // 4 bytes size + 22 bytes name + ...
                    if (fs.Read(instHead, 0, 29) < 29) break;
                    int instSize = BitConverter.ToInt32(instHead, 0);
                    string name = ReadString(instHead, 4, 22);
                    if (!string.IsNullOrWhiteSpace(name))
                        meta.SampleNames.Add(name);

                    // Number of samples in this instrument
                    int numSamples = instHead.Length >= 29 ? BitConverter.ToUInt16(instHead, 27) : 0;

                    if (numSamples > 0)
                    {
                        // Skip to end of instrument header, then skip sample headers + data
                        fs.Position = pos + instSize;
                        // Each sample header is 40 bytes; need to sum sample lengths
                        long sampleDataTotal = 0;
                        for (int s = 0; s < numSamples && fs.Position < fs.Length; s++)
                        {
                            var sh = new byte[40];
                            if (fs.Read(sh, 0, 40) < 40) break;
                            sampleDataTotal += BitConverter.ToInt32(sh, 0);
                        }
                        pos = fs.Position + sampleDataTotal;
                    }
                    else
                    {
                        pos += instSize;
                    }
                }
            }
            catch { }

            return meta;
        }

        private static TrackerMetadata ReadMod(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[1084];
            if (fs.Read(header, 0, 1084) < 1084) return new TrackerMetadata { Format = "MOD" };

            // Detect channel count from signature at offset 1080
            var sig = ReadString(header, 1080, 4);
            int channels = sig switch
            {
                "M.K." or "M!K!" or "FLT4" => 4,
                "6CHN" => 6,
                "8CHN" or "FLT8" or "OCTA" => 8,
                _ when sig.Length == 4 && sig[1] == 'C' && sig[2] == 'H' && sig[3] == 'N'
                    && char.IsDigit(sig[0]) => sig[0] - '0',
                _ when sig.Length == 4 && sig[2] == 'C' && sig[3] == 'H'
                    && char.IsDigit(sig[0]) && char.IsDigit(sig[1]) => (sig[0] - '0') * 10 + (sig[1] - '0'),
                _ => 4 // assume classic 4-channel
            };

            // Count patterns: song length is at offset 950, positions at 952..1079
            int songLen = header[950];
            int maxPattern = 0;
            for (int i = 0; i < songLen && i < 128; i++)
            {
                if (header[952 + i] > maxPattern)
                    maxPattern = header[952 + i];
            }

            var meta = new TrackerMetadata
            {
                Format = "MOD",
                Title = ReadString(header, 0, 20),
                Channels = channels,
                Patterns = maxPattern + 1,
                Instruments = 31,
                Bpm = 125,
                Tempo = 6,
            };

            // Read 31 sample names (22 bytes each, starting at offset 20)
            for (int i = 0; i < 31; i++)
            {
                string name = ReadString(header, 20 + i * 30, 22);
                if (!string.IsNullOrWhiteSpace(name))
                    meta.SampleNames.Add(name);
            }

            return meta;
        }

        private static string ReadString(byte[] data, int offset, int length)
        {
            if (offset + length > data.Length) return "";
            // Tracker strings can contain non-ASCII; replace control chars
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                byte b = data[offset + i];
                if (b == 0) break;
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
