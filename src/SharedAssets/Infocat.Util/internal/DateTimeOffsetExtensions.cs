using System;
using System.Runtime.CompilerServices;

namespace Datadog.Util
{
    internal static class DateTimeOffsetExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTimeOffset RoundDownToMinute(this DateTimeOffset dto)
        {
            return new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, 0, 0, dto.Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTimeOffset RoundDownToMinute(this DateTimeOffset dto, int setSecond)
        {
            return new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, setSecond, 0, dto.Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTimeOffset RoundDownToMinute(this DateTimeOffset dto, int setSecond, int setMillisecond)
        {
            return new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, setSecond, setMillisecond, dto.Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTimeOffset RoundDownToSecond(this DateTimeOffset dto)
        {
            return new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, dto.Second, 0, dto.Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTimeOffset RoundDownToSecond(this DateTimeOffset dto, int setMillisecond)
        {
            return new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, dto.Second, setMillisecond, dto.Offset);
        }
    }
}
