using System;

namespace FruPractice.ActBridge
{
    internal static class ActEventLine
    {
        internal static string Format(int type, DateTime timestamp, string fields)
            => $"{type:00}|{timestamp:o}|{fields}|0000000000000000";
    }
}
