using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Engine;
using Engine.Graphics;

namespace Game {
    /// <summary>
    /// HDR 环境贴图加载器，支持 Radiance .hdr 格式
    /// </summary>
    public class EnvironmentMap : IDisposable {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float[] DataFloat { get; private set; }
        public float Exposure { get; private set; } = 1.0f;

        /// <summary>
        /// 从流加载 HDR 文件
        /// </summary>
        public static EnvironmentMap LoadHDR(Stream stream) {
            using BinaryReader reader = new(stream, Encoding.ASCII, true);

            HdrHeader header = ParseHeader(reader);
            float[] data = ReadRleRgbe(reader, header.Width, header.Height);
            return new EnvironmentMap { Width = header.Width, Height = header.Height, DataFloat = data, Exposure = header.Exposure };
        }

        #region HDR 文件解析

        struct HdrHeader {
            public int Width;
            public int Height;
            public float Exposure;
            public string Format;
        }

        static HdrHeader ParseHeader(BinaryReader reader) {
            HdrHeader header = new() { Exposure = 1.0f, Format = "32-bit_rle_rgbe" };

            string line;
            bool foundFormat = false;
            while ((line = ReadLine(reader)) != null) {
                if (string.IsNullOrEmpty(line)) {
                    break;
                }

                if (line.StartsWith("FORMAT=", StringComparison.OrdinalIgnoreCase)) {
                    header.Format = line.Substring(7).Trim();
                    foundFormat = true;
                }

                if (line.StartsWith("EXPOSURE=", StringComparison.OrdinalIgnoreCase)) {
                    if (float.TryParse(line.Substring(9).Trim(), out float exp)) {
                        header.Exposure = exp;
                    }
                }
            }

            if (!foundFormat || !header.Format.Contains("32-bit_rle_rgbe", StringComparison.OrdinalIgnoreCase)) {
                throw new NotSupportedException("Only 32-bit RLE RGBE format is supported");
            }

            line = ReadLine(reader);
            if (string.IsNullOrEmpty(line)) {
                throw new InvalidDataException("Missing resolution specifier");
            }

            string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) {
                throw new InvalidDataException($"Invalid resolution specifier: {line}");
            }

            for (int i = 0; i < parts.Length - 1; i++) {
                if (parts[i].Equals("-Y", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("Y", StringComparison.OrdinalIgnoreCase)) {
                    header.Height = int.Parse(parts[i + 1]);
                }
                if (parts[i].Equals("+X", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("-X", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("X", StringComparison.OrdinalIgnoreCase)) {
                    header.Width = int.Parse(parts[i + 1]);
                }
            }
            if (header.Width <= 0 || header.Height <= 0) {
                throw new InvalidDataException($"Invalid dimensions: {header.Width}x{header.Height}");
            }
            return header;
        }

        static string ReadLine(BinaryReader reader) {
            StringBuilder sb = new();
            int b;
            while ((b = reader.ReadByte()) != -1 && b != '\n') {
                if (b != '\r') {
                    sb.Append((char)b);
                }
            }
            return b == -1 && sb.Length == 0 ? null : sb.ToString();
        }

        static float[] ReadRleRgbe(BinaryReader reader, int width, int height) {
            float[] rgbData = new float[width * height * 3];
            byte[] scanline = new byte[width * 4];
            for (int y = 0; y < height; y++) {
                int r = reader.ReadByte();
                int g = reader.ReadByte();
                int b = reader.ReadByte();
                int e = reader.ReadByte();

                if (r == 2 && g == 2 && (b & 0x80) == 0) {
                    int scanWidth = (b << 8) | e;
                    if (scanWidth != width) {
                        throw new InvalidDataException($"Scanline width mismatch: expected {width}, got {scanWidth}");
                    }

                    for (int channel = 0; channel < 4; channel++) {
                        int pos = 0;
                        while (pos < width) {
                            int code = reader.ReadByte();
                            if (code > 128) {
                                int runLength = code - 128;
                                byte value = reader.ReadByte();
                                for (int i = 0; i < runLength && pos < width; i++) {
                                    scanline[pos++ * 4 + channel] = value;
                                }
                            }
                            else {
                                for (int i = 0; i < code && pos < width; i++) {
                                    scanline[pos++ * 4 + channel] = reader.ReadByte();
                                }
                            }
                        }
                    }
                }
                else {
                    scanline[0] = (byte)r;
                    scanline[1] = (byte)g;
                    scanline[2] = (byte)b;
                    scanline[3] = (byte)e;
                    if (width > 1) {
                        reader.Read(scanline, 4, (width - 1) * 4);
                    }
                }

                for (int x = 0; x < width; x++) {
                    int srcIdx = x * 4;
                    int dstIdx = (y * width + x) * 3;
                    RgbeToFloat(scanline[srcIdx], scanline[srcIdx + 1], scanline[srcIdx + 2], scanline[srcIdx + 3], rgbData, dstIdx);
                }
            }
            return rgbData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void RgbeToFloat(byte r, byte g, byte b, byte e, float[] output, int outputIndex) {
            if (e == 0) {
                output[outputIndex] = 0;
                output[outputIndex + 1] = 0;
                output[outputIndex + 2] = 0;
            }
            else {
                float scale = MathF.Pow(2.0f, e - 136);
                output[outputIndex] = r * scale;
                output[outputIndex + 1] = g * scale;
                output[outputIndex + 2] = b * scale;
            }
        }

        #endregion

        #region 采样方法

        public (float R, float G, float B) Sample(float u, float v) {
            float x = u * Width;
            float y = v * Height;
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);

            float fx = x - x0;
            float fy = y - y0;
            int x1 = (x0 + 1) % Width;
            int y1 = Math.Clamp(y0 + 1, 0, Height - 1);
            x0 = Math.Clamp(x0, 0, Width - 1);
            y0 = Math.Clamp(y0, 0, Height - 1);
            int idx00 = (y0 * Width + x0) * 3;
            int idx10 = (y0 * Width + x1) * 3;
            int idx01 = (y1 * Width + x0) * 3;
            int idx11 = (y1 * Width + x1) * 3;
            float r = Bilerp(DataFloat[idx00], DataFloat[idx10], DataFloat[idx01], DataFloat[idx11], fx, fy);
            float g = Bilerp(DataFloat[idx00 + 1], DataFloat[idx10 + 1], DataFloat[idx01 + 1], DataFloat[idx11 + 1], fx, fy);
            float b = Bilerp(DataFloat[idx00 + 2], DataFloat[idx10 + 2], DataFloat[idx01 + 2], DataFloat[idx11 + 2], fx, fy);
            return (r, g, b);
        }

        public (float R, float G, float B) SampleDirection(System.Numerics.Vector3 direction) {
            float u = MathF.Atan2(direction.Z, direction.X) / (2 * MathF.PI) + 0.5f;
            float v = MathF.Asin(Math.Clamp(direction.Y, -1, 1)) / MathF.PI + 0.5f;
            return Sample(u, v);
        }

        static float Bilerp(float v00, float v10, float v01, float v11, float fx, float fy) {
            float v0 = v00 * (1 - fx) + v10 * fx;
            float v1 = v01 * (1 - fx) + v11 * fx;
            return v0 * (1 - fy) + v1 * fy;
        }

        #endregion

        public void Dispose() {
            DataFloat = null;
        }
    }
}
