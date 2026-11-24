using System;
using System.Globalization;

namespace HollywoodDesktop
{
    internal static class TerminalText
    {
        // Wide East Asian blocks and fullwidth forms. Ambiguous symbols such as box drawing stay narrow.
        private static readonly int[,] WideRanges =
        {
            { 0x1100, 0x115f }, { 0x2329, 0x232a }, { 0x2e80, 0x303e }, { 0x3041, 0xa4cf },
            { 0xa960, 0xa97c }, { 0xac00, 0xd7a3 }, { 0xf900, 0xfaff }, { 0xfe10, 0xfe19 },
            { 0xfe30, 0xfe6b }, { 0xff01, 0xff60 }, { 0xffe0, 0xffe6 }, { 0x16fe0, 0x18dff },
            { 0x1aff0, 0x1b2ff }, { 0x1f200, 0x1f251 }, { 0x1f300, 0x1faff }, { 0x20000, 0x3fffd }
        };

        internal static int ElementLength(string text, int index)
        {
            if (text[index] < 0x80 && (index + 1 == text.Length || text[index + 1] < 0x80)) return 1;
            return StringInfo.GetNextTextElement(text, index).Length;
        }

        internal static int Advance(string text, int index, int column)
        {
            char character = text[index];
            if (character == '\t') return 4 - (column & 3);
            if (character < 0x80) return char.IsControl(character) ? 0 : 1;

            switch (CharUnicodeInfo.GetUnicodeCategory(text, index))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                    return 0;
            }

            int scalar = char.IsSurrogatePair(text, index) ? char.ConvertToUtf32(text, index) : character;
            for (int i = 0; i < WideRanges.GetLength(0) && scalar >= WideRanges[i, 0]; i++)
                if (scalar <= WideRanges[i, 1]) return 2;
            return 1;
        }

        internal static int Columns(string text, int length)
        {
            int columns = 0;
            for (int i = 0; i < length; i += ElementLength(text, i)) columns += Advance(text, i, columns);
            return columns;
        }

        internal static string FitPadded(string text, int width)
        {
            int end = 0, columns = 0;
            while (end < text.Length)
            {
                int advance = Advance(text, end, columns);
                if (columns + advance > width) break;
                columns += advance;
                end += ElementLength(text, end);
            }
            return text.Substring(0, end) + new string(' ', width - columns);
        }
    }
}
