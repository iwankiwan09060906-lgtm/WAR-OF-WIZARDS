// QR 코드 생성 — 외부 라이브러리 없이 바이트 모드 · 오류 정정 L · 버전 1~5 (106바이트까지). 로그인 주소 하나면 충분하다.
// Unity 에 의존하지 않는다 (Tools 에서 따로 컴파일해 디코더로 시험할 수 있게).

using System;
using System.Collections.Generic;
using System.Text;

namespace SpellboundVR.UI
{
    public static class QrCode
    {
        // 버전별 (단일 블록) 데이터 · 오류 정정 바이트 수 — 레벨 L
        private static readonly int[] DataBytes = { 0, 19, 34, 55, 80, 108 };
        private static readonly int[] EccBytes = { 0, 7, 10, 15, 20, 26 };

        private static readonly byte[] Exp = new byte[512];
        private static readonly int[] Log = new int[256];

        static QrCode()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = Exp[i + 255] = (byte)x;
                Log[x] = i;
                x <<= 1;
                if (x > 255) x ^= 0x11D;
            }
        }

        /// <summary>[y, x] = 검은 칸인지. 글이 너무 길면 ArgumentException</summary>
        public static bool[,] Encode(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int version = 1;
            while (version < DataBytes.Length && DataBytes[version] < bytes.Length + 2) version++;
            if (version >= DataBytes.Length) throw new ArgumentException("QR 에 넣기에 너무 깁니다: " + text);

            byte[] data = Codewords(bytes, DataBytes[version]);
            byte[] ecc = ReedSolomon(data, EccBytes[version]);
            int size = 17 + 4 * version;
            var dark = new bool[size, size];
            var reserved = new bool[size, size];
            DrawFunctionPatterns(dark, reserved, version);
            DrawData(dark, reserved, data, ecc);

            int bestMask = 0, bestScore = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                var trial = (bool[,])dark.Clone();
                ApplyMask(trial, reserved, mask);
                DrawFormat(trial, mask);
                int score = Penalty(trial);
                if (score >= bestScore) continue;
                bestScore = score;
                bestMask = mask;
            }
            ApplyMask(dark, reserved, bestMask);
            DrawFormat(dark, bestMask);
            return dark;
        }

        private static byte[] Codewords(byte[] bytes, int capacity)
        {
            var bits = new List<bool>(capacity * 8);
            void Add(int value, int count)
            {
                for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
            }
            Add(0b0100, 4);        // 바이트 모드
            Add(bytes.Length, 8);
            foreach (byte b in bytes) Add(b, 8);
            Add(0, Math.Min(4, capacity * 8 - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(false);

            var result = new byte[capacity];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i]) result[i / 8] |= (byte)(0x80 >> (i % 8));
            for (int i = bits.Count / 8, pad = 0xEC; i < capacity; i++, pad ^= 0xEC ^ 0x11) result[i] = (byte)pad;
            return result;
        }

        private static byte[] ReedSolomon(byte[] data, int count)
        {
            var generator = new byte[count + 1];
            generator[0] = 1;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j > 0; j--)
                    generator[j] = (byte)(Multiply(generator[j], Exp[i]) ^ generator[j - 1]);
                generator[0] = Multiply(generator[0], Exp[i]);
            }
            // generator[k] = x^k 의 계수 (최고차 x^count 의 계수는 1)

            var remainder = new byte[count];
            foreach (byte b in data)
            {
                int factor = b ^ remainder[0];
                Array.Copy(remainder, 1, remainder, 0, count - 1);
                remainder[count - 1] = 0;
                for (int i = 0; i < count; i++) remainder[i] ^= Multiply((byte)factor, generator[count - 1 - i]);
            }
            return remainder;
        }

        private static byte Multiply(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

        private static void Set(bool[,] dark, bool[,] reserved, int x, int y, bool value)
        {
            dark[y, x] = value;
            reserved[y, x] = true;
        }

        private static void DrawFunctionPatterns(bool[,] dark, bool[,] reserved, int version)
        {
            int size = dark.GetLength(0);
            for (int i = 0; i < size; i++)
            {
                Set(dark, reserved, 6, i, i % 2 == 0);
                Set(dark, reserved, i, 6, i % 2 == 0);
            }
            DrawFinder(dark, reserved, 3, 3);
            DrawFinder(dark, reserved, size - 4, 3);
            DrawFinder(dark, reserved, 3, size - 4);
            if (version >= 2)
            {
                int c = size - 7;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(dark, reserved, c + dx, c + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
            for (int i = 0; i < 9; i++)
            {
                reserved[8, i] = true;
                reserved[i, 8] = true;
            }
            for (int i = 0; i < 8; i++)
            {
                reserved[8, size - 1 - i] = true;
                reserved[size - 1 - i, 8] = true;
            }
        }

        private static void DrawFinder(bool[,] dark, bool[,] reserved, int cx, int cy)
        {
            int size = dark.GetLength(0);
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    int ring = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(dark, reserved, x, y, ring != 2 && ring != 4);
                }
            }
        }

        private static void DrawData(bool[,] dark, bool[,] reserved, byte[] data, byte[] ecc)
        {
            int size = dark.GetLength(0);
            int total = (data.Length + ecc.Length) * 8;
            int i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vertical = 0; vertical < size; vertical++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vertical : vertical;
                        if (reserved[y, x] || i >= total) continue;
                        int index = i >> 3;
                        byte b = index < data.Length ? data[index] : ecc[index - data.Length];
                        dark[y, x] = ((b >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
            }
        }

        private static bool Masked(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (x / 3 + y / 2) % 2 == 0;
                case 5: return x * y % 2 + x * y % 3 == 0;
                case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
                default: return ((x + y) % 2 + x * y % 3) % 2 == 0;
            }
        }

        private static void ApplyMask(bool[,] dark, bool[,] reserved, int mask)
        {
            int size = dark.GetLength(0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (!reserved[y, x] && Masked(mask, x, y)) dark[y, x] = !dark[y, x];
        }

        private static void DrawFormat(bool[,] dark, int mask)
        {
            int size = dark.GetLength(0);
            int data = 0b01 << 3 | mask; // 레벨 L
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            bool Bit(int i) => ((bits >> i) & 1) != 0;

            for (int i = 0; i <= 5; i++) dark[i, 8] = Bit(i);
            dark[7, 8] = Bit(6);
            dark[8, 8] = Bit(7);
            dark[8, 7] = Bit(8);
            for (int i = 9; i < 15; i++) dark[8, 14 - i] = Bit(i);
            for (int i = 0; i < 8; i++) dark[8, size - 1 - i] = Bit(i);
            for (int i = 8; i < 15; i++) dark[size - 15 + i, 8] = Bit(i);
            dark[size - 8, 8] = true;
        }

        /// <summary>마스크 고르기용 벌점 — 같은 색 줄 · 같은 색 2×2 · 찾기 무늬 비슷한 줄 · 검은 칸 비율</summary>
        private static int Penalty(bool[,] dark)
        {
            int size = dark.GetLength(0);
            int score = 0, black = 0;
            for (int a = 0; a < size; a++)
            {
                int runRow = 1, runCol = 1;
                int windowRow = 0, windowCol = 0;
                for (int b = 0; b < size; b++)
                {
                    if (dark[a, b]) black++;
                    if (b > 0)
                    {
                        runRow = dark[a, b] == dark[a, b - 1] ? runRow + 1 : 1;
                        runCol = dark[b, a] == dark[b - 1, a] ? runCol + 1 : 1;
                        if (runRow == 5) score += 3;
                        else if (runRow > 5) score++;
                        if (runCol == 5) score += 3;
                        else if (runCol > 5) score++;
                    }
                    if (a + 1 < size && b + 1 < size && dark[a, b] == dark[a, b + 1] && dark[a, b] == dark[a + 1, b] && dark[a, b] == dark[a + 1, b + 1])
                        score += 3;
                    windowRow = (windowRow << 1 | (dark[a, b] ? 1 : 0)) & 0x7FF;
                    windowCol = (windowCol << 1 | (dark[b, a] ? 1 : 0)) & 0x7FF;
                    if (b >= 10 && (windowRow == 0x5D0 || windowRow == 0x05D)) score += 40;
                    if (b >= 10 && (windowCol == 0x5D0 || windowCol == 0x05D)) score += 40;
                }
            }
            score += Math.Abs(black * 20 / (size * size) - 10) * 10;
            return score;
        }
    }
}
