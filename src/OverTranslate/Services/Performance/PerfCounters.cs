namespace OverTranslate.Services.Performance;

/// <summary>
/// The few numbers the performance panel shows that nothing else keeps. Plain atomic counters:
/// recording one is a single interlocked add, so they cost nothing to leave on and the panel samples
/// them only while it is open.
/// </summary>
internal static class PerfCounters
{
    private static long _ocrRuns;
    private static int _lastOcrMs;
    private static int _pendingTranslations;
    private static long _cacheHits;
    private static long _cacheMisses;
    private static int _lastTranslationMs;

    public static long OcrRuns => Interlocked.Read(ref _ocrRuns);
    public static int LastOcrMs => Volatile.Read(ref _lastOcrMs);
    public static int PendingTranslations => Volatile.Read(ref _pendingTranslations);
    public static long CacheHits => Interlocked.Read(ref _cacheHits);
    public static long CacheMisses => Interlocked.Read(ref _cacheMisses);
    public static int LastTranslationMs => Volatile.Read(ref _lastTranslationMs);

    public static void RecordOcr(int milliseconds)
    {
        Volatile.Write(ref _lastOcrMs, milliseconds);
        Interlocked.Increment(ref _ocrRuns);
    }

    public static void TranslationStarted() => Interlocked.Increment(ref _pendingTranslations);

    public static void TranslationFinished(int milliseconds)
    {
        Volatile.Write(ref _lastTranslationMs, milliseconds);
        Interlocked.Decrement(ref _pendingTranslations);
    }

    public static void RecordCache(int hits, int misses)
    {
        if (hits > 0) Interlocked.Add(ref _cacheHits, hits);
        if (misses > 0) Interlocked.Add(ref _cacheMisses, misses);
    }
}
