namespace WorkIsland.Core;

public static class ReminderTiming
{
    public static double Frequency(double elapsed, double threshold)
    {
        if (!RuntimeCompat.IsFinite(elapsed) || !RuntimeCompat.IsFinite(threshold) || threshold <= 0 || elapsed < threshold) return 0;
        return Math.Min(1.5, Math.Pow(2, Math.Min(1023, Math.Floor(elapsed / threshold))) / threshold);
    }
    public static double Intensity(double elapsed, double threshold)
    {
        var frequency = Frequency(elapsed, threshold);
        if (frequency <= 0) return 0;
        var round = Math.Floor(elapsed / threshold);
        var completed = 1d; var cycles = 0d;
        while (completed < round)
        {
            var rate = Frequency(completed * threshold, threshold);
            if (rate >= 1.5) break;
            cycles += threshold * rate; completed++;
        }
        cycles += Math.Max(0, round - completed) * threshold * 1.5;
        cycles += elapsed % threshold * frequency;
        var timeInPulse = cycles % 1 / frequency;
        var duration = Math.Min(1.5, .9 / frequency);
        return timeInPulse < duration ? (1 - Math.Cos(timeInPulse / duration * Math.PI * 2)) / 2 : 0;
    }
}
