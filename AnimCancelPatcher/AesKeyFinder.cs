using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AnimCancelPatcher
{
    public static class AesKeyFinder //I am a Skidder(;^ω^) https://github.com/GHFear/AESDumpster
    {
        private static readonly string[] Patterns = {
            "C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ?",
            "C7 ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ?",
            "C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? 48 ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ?",
            "C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? C3"
        };

        private static readonly int[][] DwordOffsets = {
            new[] { 3, 10, 17, 24, 35, 42, 49, 56 },
            new[] { 2, 9, 16, 23, 30, 37, 44, 51 },
            new[] { 3, 10, 21, 28, 35, 42, 49, 56 },
            new[] { 51, 45, 38, 31, 24, 17, 10, 3 }
        };

        private static readonly HashSet<string> FalsePositives = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9FFD9",
            "67E6096A85AE67BB72F36E3C3AF54FA57F520E518C68059BABD9831F19CDE05B",
            "D89E05C107D57C3617DD703039590EF7310BC0FF11155868A78FF964A44FFABE",
            "9A99593F9A99593F0AD7633F52B8BE3FE17A543FCDCC4C3D4260E53BAE47A13F",
            "6F168073B9B21449D742241700068ADABC306FA9AA3831164DEE8DE34E0EFBB0",
            "0AD7633FCDCC4C3DCDCCCC3D52B8BE3F9A99593F9A99593FC9767E3FE17A543F",
            "168073C7B21449C7430C00064310BC304314AA3843184DEE431C4E0E83C4205B",
            "E6096AC7AE67BBC7430C3AF543107F5243148C684318ABD9431C19CD436C2000",
            "9E05C1C7D57C36C7430C39594310310B431411154318A78F431CA44F436C1C00",
            "9E05C1C7D57C36C7DD7030C7590EF7C70BC0FFC7155868C78FF964C7A44FFABE",
            "168073C7B21449C7422417C7068ADAC7306FA9C7383116C7EE8DE3C74E0EFBB0",
            "0AD7633FCDCC4C3D00C742143DC742183FC7421C3FC742203FC742247E3FC742",
            "0000803F0AD7A33E0AD7633F52B8BE3FE17A543FCDCC4C3D4260E53B54AE47A1",
            "0AD7A33E0AD7633F52B8BE3FE17A543FCDCC4C3D4260E53BAE47A13F58583934",
            "0AD7A33E0AD7633F52B8BE3FE17A543FCDCC4C3D4260E53BAE47A13F38583934",
            "0000803F0AD7A33E0AD7633F52B8BE3FE17A543FCDCC4C3D4260E53B34AE47A1",
            "0000803F0000803F0AD7A33E0AD7633F52B8BE3FE17A543FCDCC4C3D2C4260E5",
            "0AD7633F52B8BE3FE17A543FCDCC4C3D4260E53BAE47A13F5839343C4CC9767E",
            "0AD7633F52B8BE3FE17A543FCDCC4C3D4260E53BAE47A13F5839343C4CC9767E",
            "07D57C3617DD703039590EF7310BC0FF11155868A78FF964A44FFABE6C1C0000",
            "85AE67BB72F36E3C3AF54FA57F520E518C68059BABD9831F19CDE05B6C200000",
            "E6096AC7AE67BBC7F36E3CC7F54FA5C7520E51C768059BC7D9831FC719CDE05B",
            "0AD7A33E0AD7633F52B8BE3FE17A543FCDCC4C3D4260E53BAE47A13F3C583934",
            "E4D6E74FE4D667500044AC47926595380080DC43000A9B46000080BF000080BF",
            "D04C8F7D71ECC047D8A60970FBA31C9E9EC1250BBBF6459AC480947212E1DB8C"
        };

        public static string FindKey(string exePath)
        {
            if (!File.Exists(exePath)) return null;

            byte[] buffer;
            try
            {
                buffer = File.ReadAllBytes(exePath);
            }
            catch
            {
                return null;
            }

            var candidates = new List<string>();
            //var sw = new Stopwatch(); sw.Start();

            for (int i = 0; i < Patterns.Length; i++)
            {
                var matches = SearchPattern(buffer, Patterns[i]);
                foreach (var offset in matches)
                {
                    string key = ExtractKey(buffer, offset, DwordOffsets[i]);
                    candidates.Add(key);
                }
            }

            string bestKey = null;
            double bestEntropy = -1;

            foreach (var key in candidates)
            {
                if (FalsePositives.Contains(key)) continue;

                double entropy = CalcEntropy(key);
                if (entropy >= 3.3 && entropy > bestEntropy)
                {
                    bestEntropy = entropy;
                    bestKey = key;
                }
            }

            if (bestKey != null)
            {
                return "0x" + bestKey.ToUpperInvariant();
            }
            return null;
        }

        private static List<int> SearchPattern(byte[] data, string patternStr)
        {
            var parts = patternStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var pattern = new short[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "?" || parts[i] == "??")
                {
                    pattern[i] = -1;
                }
                else
                {
                    pattern[i] = Convert.ToInt16(parts[i], 16);
                }
            }

            var results = new List<int>();
            int limit = data.Length - pattern.Length;
            for (int i = 0; i < limit; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (pattern[j] != -1 && data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) results.Add(i);
            }
            return results;
        }

        private static string ExtractKey(byte[] data, int startOffset, int[] offsets)
        {
            StringBuilder sb = new StringBuilder(64);
            foreach (int offset in offsets)
            {
                for (int i = 0; i < 4; i++)
                {
                    byte b = data[startOffset + offset + i];
                    string hex = b.ToString("X2");
                    sb.Append(hex);
                }
            }
            return sb.ToString();
        }

        private static double CalcEntropy(string s)
        {
            var map = new Dictionary<char, int>();
            foreach (char c in s)
            {
                if (!map.ContainsKey(c)) map[c] = 0;
                map[c] = map[c] + 1;
            }

            double entropy = 0;
            int totalChars = 0;
            foreach (var kvp in map)
            {
                totalChars += kvp.Value;
            }

            foreach (var kvp in map)
            {
                double freq = (double)kvp.Value / totalChars;
                entropy += freq * Math.Log(freq, 2);
            }
            return -entropy;
        }
    }
}